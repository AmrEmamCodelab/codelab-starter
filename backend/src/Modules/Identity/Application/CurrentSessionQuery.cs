using Starter.Application.Users;
using Starter.Modules.Identity.Domain;

namespace Starter.Modules.Identity.Application;

/// <summary>The caller's session as the logout paths need it.</summary>
/// <param name="SessionId">The session id (<c>sid</c>).</param>
/// <param name="UserId">The internal user id.</param>
/// <param name="Mode">How the session was signed in.</param>
/// <param name="IdTokenHint">For an SSO session, the provider's id_token for its end-session call; otherwise null.</param>
/// <param name="IsEnded">True when the session was already ended.</param>
public sealed record CurrentSession(string SessionId, long UserId, SignInMode Mode, string? IdTokenHint, bool IsEnded)
{
    /// <summary>A log-safe description: the id_token is never printed.</summary>
    public override string ToString() => nameof(CurrentSession) + "(" + SessionId + ", " + Mode + ")";
}

/// <summary>Looks up the session named by the caller's access token (<c>sid</c>): how it was signed in and its logout hint.</summary>
public sealed class CurrentSessionQuery(ICurrentUserContext currentUser, IUserSessionRepository sessions)
{
    /// <summary>The caller's session, or null when the token names none or no stored session has that id.</summary>
    public async Task<CurrentSession?> FindAsync(CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(currentUser.SessionId))
        {
            return null;
        }

        var session = await sessions.FindBySessionIdAsync(currentUser.SessionId, cancellationToken);
        return session is null
            ? null
            : new CurrentSession(
                SessionId: session.SessionId,
                UserId: session.UserId,
                Mode: session.SignInMode,
                IdTokenHint: session.IdTokenHint,
                IsEnded: session.RevokedAt is not null);
    }
}
