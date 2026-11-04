using Microsoft.AspNetCore.Http;

namespace Codelab.Observability;

/// <summary>
/// Allocates the correlation id near the top of the pipeline, echoes it on EVERY response (not only failures) and
/// pushes <see cref="LogPropertyNames.CorrelationId"/> and <see cref="LogPropertyNames.ReferenceCode"/> onto the
/// LogContext, so every event raised downstream carries them without a call site changing.
/// <para>
/// Registered by <see cref="CodelabObservabilityExtensions.UseCodelabObservability"/>, outside the exception handler
/// and status-code pages, which also call <see cref="CorrelationId.For"/> themselves and log the id explicitly.
/// </para>
/// </summary>
public sealed class CorrelationIdMiddleware(RequestDelegate next)
{
    /// <summary>Runs the middleware.</summary>
    public async Task InvokeAsync(HttpContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        _ = CorrelationId.For(context);

        // OnStarting: headers must be written before the response is committed, and this is the last safe moment.
        context.Response.OnStarting(static state =>
        {
            var http = (HttpContext)state;
            http.Response.Headers[CorrelationId.Header] = CorrelationId.For(http);
            return Task.CompletedTask;
        }, context);

        using (CorrelationLogProperties.PushTo(context))
        {
            await next(context).ConfigureAwait(false);
        }
    }
}
