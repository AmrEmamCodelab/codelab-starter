using Codelab.Errors;
using Codelab.Security.RateLimiting;
using Codelab.Security.Signing;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Logging;

namespace Starter.Infrastructure.Security;

/// <summary>
/// <c>POST /api/security/csp-reports</c>: the sink of the SPA document's CSP violation reports. Anonymous by necessity
/// (a browser posts a report without the page's credentials, so an authorized sink would receive nothing), exempt
/// from request signing (the browser, not the SPA, sends it), rate limited per client, and capped at
/// <see cref="CspReportContract.MaxBodyBytes"/>. It accepts <c>application/csp-report</c> and
/// <c>application/reports+json</c>, logs one <see cref="CspViolationLog.EventName"/> event per violation and stores
/// nothing. Answers 204, 413 (body over the cap), 415 (other content type), 400 (not a report) or 429.
/// </summary>
public static class CspReportEndpoint
{
    /// <summary>Why the endpoint is anonymous and unsigned (reviewed with the anonymous allowlist).</summary>
    public const string AnonymousReason =
        "Browsers post CSP violation reports without the page's credentials or request proof; the sink only logs redacted fields.";

    /// <summary>Maps the sink.</summary>
    public static IEndpointConventionBuilder MapCspReportSink(this IEndpointRouteBuilder endpoints)
    {
        ArgumentNullException.ThrowIfNull(endpoints);
        return endpoints.MapPost(SecurityEdgeRoutes.CspReports, ReceiveAsync)
            .AllowAnonymous()
            .RequireRateLimiting(ClientRateLimitOptions.PolicyName)
            .WithSignatureExemption(SignatureExemption.WholeRequest, AnonymousReason)
            .Produces(StatusCodes.Status204NoContent)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status413PayloadTooLarge)
            .ProducesProblem(StatusCodes.Status415UnsupportedMediaType)
            .ProducesProblem(StatusCodes.Status429TooManyRequests);
    }

    private static async Task<IResult> ReceiveAsync(HttpContext context, ILoggerFactory loggers)
    {
        var request = context.Request;
        if (CspReportContract.FormatOf(request.ContentType) is not { } format)
        {
            return Problem(StatusCodes.Status415UnsupportedMediaType);
        }

        if (request.ContentLength > CspReportContract.MaxBodyBytes)
        {
            return Problem(StatusCodes.Status413PayloadTooLarge);
        }

        var body = await BoundedBody.ReadAsync(request.Body, CspReportContract.MaxBodyBytes, context.RequestAborted);
        if (body is null)
        {
            return Problem(StatusCodes.Status413PayloadTooLarge);
        }

        if (!CspReportNormalizer.TryNormalize(body.Value, format, out var violations))
        {
            return Problem(StatusCodes.Status400BadRequest);
        }

        var logger = loggers.CreateLogger(CspViolationLog.Category);
        foreach (var violation in violations)
        {
            CspViolationLog.Write(logger, violation);
        }

        return TypedResults.NoContent();
    }

    private static ProblemEnvelopeResult Problem(int status)
    {
        var description = FrameworkStatusCodes.Describe(status);
        var problem = new ProblemDetails { Status = status, Detail = description.Detail };
        problem.Extensions[ErrorEnvelope.CodeMember] = description.Code;
        return new ProblemEnvelopeResult(problem);
    }
}
