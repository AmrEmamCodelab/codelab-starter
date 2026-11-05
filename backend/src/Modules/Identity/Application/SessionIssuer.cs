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

    /// <summary>
    /// Starts a session for <paramref name="account"/> under the user's current session version, recording how it was
    /// signed in (and, for SSO, the identity provider's id_token as the logout hint). Saves the Identity unit of work,
    /// so pending changes to the account (a reset failure counter, a rehash) are written with it.
    /// </summary>
    public async Task<LoginResult> StartAsync(UserAccount account, SessionStart start, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(account);
        ArgumentNullException.ThrowIfNull(start);
        var now = tokens.Now();
        var version = await versions.CurrentAsync(account.SubjectId, cancellationToken);
        var secret = tokens.NewRefreshSecret();
        var session = UserSession.Start(new NewUserSession(
            SessionId: tokens.NewSessionId(),
            UserId: account.Id,
            RefreshSecret: secret,
            SessionVersion: version,
            StartedAt: now,
            Lifetime: tokens.Settings.RefreshTokenLifetime,
            Mode: start.Mode,
            IdTokenHint: start.IdTokenHint));
        sessions.Add(session);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return tokens.Issue(account, session, secret, now);
    }

    /// <summary>
    /// True while <paramref name="session"/> was issued under the user's current session version (no deactivation,
    /// password reset or activation since).
    /// </summary>
    public async Task<bool> IsCurrentAsync(UserAccount account, UserSession session, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(account);
        ArgumentNullException.ThrowIfNull(session);
        return await versions.CurrentAsync(account.SubjectId, cancellationToken) <= session.SessionVersion;
    }

    /// <summary>Rotates the refresh secret of <paramref name="session"/> and issues a new access token.</summary>
    public async Task<LoginResult> RotateAsync(UserAccount user, UserSession session, CancellationToken cancellationToken)
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

/// <summary>How a session starts: the sign-in mode and, for SSO, the identity provider's id_token kept as the logout hint.</summary>
/// <param name="Mode">How the user signed in.</param>
/// <param name="IdTokenHint">The id_token for the provider's end-session call, or null (basic and directory sign-ins).</param>
public sealed record SessionStart(SignInMode Mode, string? IdTokenHint)
{
    /// <summary>A basic (user name and password) sign-in.</summary>
    public static SessionStart Basic { get; } = new(SignInMode.Basic, IdTokenHint: null);

    /// <summary>A log-safe description: the id_token is never printed.</summary>
    public override string ToString() => nameof(SessionStart) + "(" + Mode + ")";
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
    public LoginResult Issue(UserAccount user, UserSession session, string secret, DateTimeOffset now)
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
        return new LoginResult(
            AccessToken: token,
            AccessTokenExpiresAt: expiresAt,
            RefreshToken: new RefreshToken(session.SessionId, secret).Format(),
            IdleTimeoutMinutes: (int)settings.IdleTimeout.TotalMinutes,
            UserId: user.Id,
            DisplayName: user.DisplayName);
    }
}
