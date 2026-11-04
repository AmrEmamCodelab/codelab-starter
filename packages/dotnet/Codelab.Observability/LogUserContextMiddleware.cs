using Microsoft.AspNetCore.Http;
using Serilog;
using Serilog.Context;

namespace Codelab.Observability;

/// <summary>
/// Puts the caller's <see cref="LogPropertyNames.UserIdHash"/> on the request's log lines: on the LogContext for
/// every event raised downstream, and on the request line through the diagnostic context. Nothing is added for an
/// anonymous caller; the raw NameIdentifier is never added. Registered after <c>UseAuthentication</c> by
/// <see cref="CodelabObservabilityExtensions.UseCodelabUserLogContext"/>.
/// </summary>
public sealed class LogUserContextMiddleware(RequestDelegate next, UserIdHash userIdHash, IDiagnosticContext diagnosticContext)
{
    /// <summary>Runs the middleware.</summary>
    public async Task InvokeAsync(HttpContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        if (userIdHash.Of(context.User) is not { } hash)
        {
            await next(context).ConfigureAwait(false);
            return;
        }

        diagnosticContext.Set(LogPropertyNames.UserIdHash, hash);
        using (LogContext.PushProperty(LogPropertyNames.UserIdHash, hash))
        {
            await next(context).ConfigureAwait(false);
        }
    }
}
