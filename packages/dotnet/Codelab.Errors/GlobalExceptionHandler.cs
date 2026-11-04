using System.Diagnostics;
using System.Globalization;
using Codelab.Observability;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;

namespace Codelab.Errors;

/// <summary>
/// The single failure surface of a Codelab host and the ONLY <see cref="IExceptionHandler"/> registered
/// (<c>AddCodelabErrors</c>). Every exception that escapes an endpoint, a filter or the authorization layer leaves as
/// (a) ONE structured log line with the full exception and (b) one envelope with <c>code</c>, <c>correlationId</c> and
/// <c>referenceCode</c>, never an exception message, a type name or a stack frame.
/// <para>
/// It owns what HAPPENS to a request (log once at the class's severity, bail if the response started, write the
/// envelope); what an exception MEANS is the ordered classifier table's (<see cref="FailureClassifierTable"/>).
/// Background work never reaches it: the <see cref="JobFailureLogFilter"/> covers jobs with the same table.
/// </para>
/// <para>
/// PII rule: the line carries identifiers only (correlation id, method, path without query, route template, the keyed
/// <see cref="UserIdHash"/>), never request content. The exception text is redacted by the retained sinks.
/// </para>
/// </summary>
public sealed partial class GlobalExceptionHandler(
    FailureClassifierTable classifiers,
    UserIdHash userIdHash,
    ILogger<GlobalExceptionHandler> logger) : IExceptionHandler
{
    /// <summary>The span tag that carries the failure's code (also the status description of a server fault).</summary>
    public const string ErrorCodeSpanTag = "codelab.error_code";

    private const string RequestMethodProperty = "Method";
    private const string RequestPathProperty = "Path";

    private const string FailureTemplate =
        "Request failed {CorrelationId} {ReferenceCode} {Code} {StatusCode} {Method} {Path} {Classifier} {ErrorFingerprint} {ExceptionType}";

    /// <inheritdoc />
    public async ValueTask<bool> TryHandleAsync(HttpContext httpContext, Exception exception, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(httpContext);
        ArgumentNullException.ThrowIfNull(exception);

        // The caller went away: no socket to write to, and logging it as an error is noise. Handled, but silent.
        if (exception is OperationCanceledException && httpContext.RequestAborted.IsCancellationRequested)
        {
            var abandonedId = CorrelationId.For(httpContext);
            LogAbandoned(logger, abandonedId, httpContext.Request.Method);
            return true;
        }

        var classification = classifiers.Classify(exception, httpContext);
        LogFailure(httpContext, exception, classification);
        MarkSpan(httpContext, classification.Failure);

        // Once the first byte is on the wire the status and body are fixed; the line above records the failure.
        if (httpContext.Response.HasStarted)
        {
            return true;
        }

        await WriteEnvelopeAsync(httpContext, classification.Failure).ConfigureAwait(false);
        return true;
    }

    private void LogFailure(HttpContext httpContext, Exception exception, Classification classification)
    {
        var correlationId = CorrelationId.For(httpContext);
        var failure = classification.Failure;
        var state = new StructuredLogState(FailureTemplate,
        [
            KeyValuePair.Create(LogPropertyNames.CorrelationId, (object?)correlationId),
            KeyValuePair.Create(LogPropertyNames.ReferenceCode, (object?)ReferenceCode.From(correlationId)),
            KeyValuePair.Create(LogPropertyNames.Code, (object?)failure.Code),
            KeyValuePair.Create(LogPropertyNames.StatusCode, (object?)failure.Status),
            KeyValuePair.Create(RequestMethodProperty, (object?)httpContext.Request.Method),
            KeyValuePair.Create(RequestPathProperty, (object?)httpContext.Request.Path.Value),
            KeyValuePair.Create(LogPropertyNames.Classifier, (object?)classification.Classifier),
            KeyValuePair.Create(LogPropertyNames.ErrorFingerprint, (object?)FingerprintOf(httpContext, exception)),
            KeyValuePair.Create(LogPropertyNames.ExceptionType, (object?)exception.GetType().FullName),
        ]);

        using (logger.BeginScope(FailureScope(httpContext)))
        {
            logger.Log(failure.Level, default, state, exception, StructuredLogState.Format);
        }
    }

    /// <summary>The scope adds <see cref="LogPropertyNames.UserIdHash"/> and <see cref="LogPropertyNames.RouteTemplate"/> only when they exist.</summary>
    private List<KeyValuePair<string, object>> FailureScope(HttpContext httpContext)
    {
        var scope = new List<KeyValuePair<string, object>>();
        if (userIdHash.Of(httpContext.User) is { } hash)
        {
            scope.Add(new(LogPropertyNames.UserIdHash, hash));
        }

        if (FailureFingerprint.RouteTemplateOf(httpContext) is { } routeTemplate)
        {
            scope.Add(new(LogPropertyNames.RouteTemplate, routeTemplate));
        }

        return scope;
    }

    private static string FingerprintOf(HttpContext httpContext, Exception exception) =>
        FailureFingerprint.For(new FingerprintInput(exception.GetType(), FailureFingerprint.RequestSite(httpContext)));

    /// <summary>Only the bug class marks the request span failed, with the code and never the message.</summary>
    private static void MarkSpan(HttpContext httpContext, Failure failure)
    {
        var activity = httpContext.Features.Get<IHttpActivityFeature>()?.Activity ?? Activity.Current;
        activity?.SetTag(ErrorCodeSpanTag, failure.Code);
        if (failure.IsServerFault)
        {
            activity?.SetStatus(ActivityStatusCode.Error, failure.Code);
        }
    }

    private static Task WriteEnvelopeAsync(HttpContext httpContext, Failure failure)
    {
        httpContext.Response.StatusCode = failure.Status;
        httpContext.Response.Headers[CorrelationId.Header] = CorrelationId.For(httpContext);
        if (failure.RetryAfterSeconds is { } retryAfter)
        {
            httpContext.Response.Headers.RetryAfter = retryAfter.ToString(CultureInfo.InvariantCulture);
        }

        ClientErrorScrubber.MarkVetted(httpContext, failure.Detail);
        var problem = new ProblemDetails
        {
            Status = failure.Status,
            Title = ErrorEnvelope.Title,
            Detail = failure.Detail,
            Extensions = { [ErrorEnvelope.CodeMember] = failure.Code },
        };
        ErrorEnvelope.StampIdentity(problem.Extensions, httpContext);
        return ProblemResponseWriter.WriteAsync(httpContext, problem);
    }

    [LoggerMessage(Level = LogLevel.Debug, Message = "Request {CorrelationId} {Method} was abandoned by the caller.")]
    private static partial void LogAbandoned(ILogger logger, string correlationId, string method);
}
