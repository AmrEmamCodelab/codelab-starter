using Microsoft.AspNetCore.Http;
using Serilog;
using Serilog.Context;
using Serilog.Core;
using Serilog.Core.Enrichers;

namespace Codelab.Observability;

/// <summary>
/// The id properties a request's log lines carry, in one place, so the ambient scope and the request line cannot
/// disagree: <see cref="LogPropertyNames.CorrelationId"/> and <see cref="LogPropertyNames.ReferenceCode"/> always,
/// <see cref="LogPropertyNames.InboundCorrelationId"/> when the caller sent a well-formed, differing id.
/// </summary>
public static class CorrelationLogProperties
{
    /// <summary>Pushes the properties onto Serilog's <see cref="LogContext"/> until the result is disposed.</summary>
    public static IDisposable PushTo(HttpContext context) =>
        LogContext.Push(For(context).Select(ILogEventEnricher (p) => new PropertyEnricher(p.Name, p.Value)).ToArray());

    /// <summary>
    /// Sets the properties on the request line, which is written outside every middleware's LogContext scope
    /// (request logging is outermost), so it needs them explicitly.
    /// </summary>
    public static void EnrichRequestLine(IDiagnosticContext diagnosticContext, HttpContext context)
    {
        ArgumentNullException.ThrowIfNull(diagnosticContext);

        foreach (var (name, value) in For(context))
        {
            diagnosticContext.Set(name, value);
        }
    }

    private static IEnumerable<(string Name, string Value)> For(HttpContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        var correlationId = CorrelationId.For(context);
        yield return (LogPropertyNames.CorrelationId, correlationId);
        yield return (LogPropertyNames.ReferenceCode, ReferenceCode.From(correlationId));

        if (CorrelationId.InboundFor(context) is { } inbound)
        {
            yield return (LogPropertyNames.InboundCorrelationId, inbound);
        }
    }
}
