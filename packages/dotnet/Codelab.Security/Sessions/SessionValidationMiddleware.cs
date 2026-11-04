using Codelab.Security.Problems;
using Microsoft.Extensions.Options;

namespace Codelab.Security.Sessions;

/// <summary>
/// Checks every authenticated user request against the session stores: a revoked <c>sid</c> is refused 401
/// <c>session_revoked</c>, a token whose <c>sver</c> is behind the user's current version is refused 401
/// <c>session_version_stale</c>, and a user token with no <c>sid</c> is refused 401 <c>session_unidentified</c>
/// (it could never be revoked). A store failure refuses 503 (fail closed). Service principals are not checked.
/// </summary>
public sealed class SessionValidationMiddleware(RequestDelegate next, IOptionsMonitor<SecuritySessionOptions> options, ILogger<SessionValidationMiddleware> logger)
{
    private const int StoreRetryAfterSeconds = 5;

    /// <summary>Runs the middleware.</summary>
    public async Task InvokeAsync(HttpContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        var settings = options.CurrentValue;
        if (!settings.ValidateOnEveryRequest || context.User.Identity?.IsAuthenticated != true || SessionClaims.IsService(context.User))
        {
            await next(context);
            return;
        }

        string? refusal;
        try
        {
            refusal = await CheckAsync(context, settings);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            SecurityLog.SessionCheckUnavailable(logger, ex, context.Request.Method, context.Request.Path);
            var unavailable = new SecurityProblem(StatusCodes.Status503ServiceUnavailable, "The session could not be checked.", Code: null) { RetryAfterSeconds = StoreRetryAfterSeconds };
            await SecurityProblemWriter.TryWriteAsync(context, unavailable);
            return;
        }

        if (refusal is null)
        {
            await next(context);
            return;
        }

        SecurityLog.SessionRejected(logger, context.Request.Method, context.Request.Path, refusal);
        await SecurityProblemWriter.TryWriteAsync(context, new SecurityProblem(StatusCodes.Status401Unauthorized, "The session is no longer valid.", refusal));
    }

    private static async Task<string?> CheckAsync(HttpContext context, SecuritySessionOptions settings)
    {
        var services = context.RequestServices;
        var sessionId = SessionClaims.Value(context.User, [settings.SessionIdClaimType]);
        if (sessionId.Length == 0)
        {
            return settings.RequireSessionId ? SecurityProblemCodes.SessionUnidentified : null;
        }

        if (await services.GetRequiredService<ISessionRevocationStore>().IsRevokedAsync(sessionId, context.RequestAborted))
        {
            return SecurityProblemCodes.SessionRevoked;
        }

        var userId = SessionClaims.UserId(context.User, settings);
        var tokenVersion = SessionClaims.Version(context.User, settings) ?? 0;
        if (userId.Length == 0)
        {
            return null;
        }

        var current = await services.GetRequiredService<ISessionVersionStore>().GetVersionAsync(userId, context.RequestAborted);
        return tokenVersion < current ? SecurityProblemCodes.SessionVersionStale : null;
    }
}
