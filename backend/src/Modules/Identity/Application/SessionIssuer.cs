using Starter.Modules.Identity.Domain;

namespace Starter.Modules.Identity.Application;

/// <summary>
/// The ONE session issuer: starts a session (login), rotates it (refresh) and ends it (logout, or a refused refresh
/// that looks like theft). Every access token it issues carries the session id and the session version; every
/// refresh token is <c>&lt;sid&gt;.&lt;secret&gt;</c> with only the secret's hash stored.
/// </summary>
public sealed class SessionIssuer(
    IUserSessionRepository sessions,
    IIdentityUnitOfWork unitOfWork,
    SessionTokens tokens,
    ISessionVersions versions,
    ISessionRevocation revocation)
{
    /// <summary>The session lifetimes.</summary>
    public SessionSettings Settings => tokens.Settings;

    /// <summary>The current time.</summary>
    public DateTimeOffset Now() => tokens.Now();

    /// <summary>Starts a session for <paramref name="user"/> under the user's current session version.</summary>
    public async Task<SignedInSession> StartAsync(UserAccount user, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(user);
        var now = tokens.Now();
        var version = await versions.CurrentAsync(user.SubjectId, cancellationToken);
        var secret = tokens.NewRefreshSecret();
        var session = UserSession.Start(new NewUserSession(
            SessionId: tokens.NewSessionId(),
            UserId: user.Id,
            RefreshSecret: secret,
            SessionVersion: version,
            StartedAt: now,
            Lifetime: tokens.Settings.RefreshTokenLifetime));
        sessions.Add(session);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return tokens.Issue(user, session, secret, now);
    }

    /// <summary>Rotates the refresh secret of <paramref name="session"/> and issues a new access token.</summary>
    public async Task<SignedInSession> RotateAsync(UserAccount user, UserSession session, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(user);
        ArgumentNullException.ThrowIfNull(session);
        var now = tokens.Now();
        var secret = tokens.NewRefreshSecret();
        session.Rotate(secret, now);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return tokens.Issue(user, session, secret, now);
    }

    /// <summary>
    /// Ends <paramref name="session"/>: no refresh works any more, and its session id is revoked for as long as an
    /// access token of it can still be valid, so a replayed token is refused (401) on the very next request.
    /// </summary>
    public async Task EndAsync(UserSession session, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(session);
        session.Revoke(tokens.Now());
        await unitOfWork.SaveChangesAsync(cancellationToken);
        await RevokeSessionIdAsync(session.SessionId, cancellationToken);
    }

    /// <summary>Revokes a session id that has no stored session (its access tokens still stop working).</summary>
    public Task RevokeSessionIdAsync(string sessionId, CancellationToken cancellationToken) =>
        revocation.RevokeAsync(sessionId, tokens.Now() + tokens.Settings.AccessTokenLifetime, cancellationToken);
}

/// <summary>
/// The token side of the session issuer: the clock, the lifetimes, random ids and secrets, and the signed access token
/// plus refresh token of a session.
/// </summary>
public sealed class SessionTokens(IAccessTokenIssuer accessTokens, ISecureTokenGenerator generator, SessionSettings settings, TimeProvider clock)
{
    /// <summary>The session lifetimes.</summary>
    public SessionSettings Settings => settings;

    /// <summary>The current time.</summary>
    public DateTimeOffset Now() => clock.GetUtcNow();

    /// <summary>A new session id.</summary>
    public string NewSessionId() => generator.NewSessionId();

    /// <summary>A new refresh secret.</summary>
    public string NewRefreshSecret() => generator.NewRefreshSecret();

    /// <summary>The tokens of <paramref name="session"/> issued at <paramref name="now"/>.</summary>
    public SignedInSession Issue(UserAccount user, UserSession session, string secret, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(user);
        ArgumentNullException.ThrowIfNull(session);
        var expiresAt = now + settings.AccessTokenLifetime;
        var token = accessTokens.Issue(new AccessTokenClaims(
            SubjectId: user.SubjectId,
            SessionId: session.SessionId,
            SessionVersion: session.SessionVersion,
            IssuedAt: now,
            ExpiresAt: expiresAt));
        return new SignedInSession(
            AccessToken: token,
            AccessTokenExpiresAt: expiresAt,
            RefreshToken: new RefreshToken(session.SessionId, secret).Format(),
            IdleTimeoutMinutes: (int)settings.IdleTimeout.TotalMinutes,
            UserId: user.Id,
            DisplayName: user.DisplayName);
    }
}
