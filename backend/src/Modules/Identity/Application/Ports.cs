using Starter.Application.Persistence;
using Starter.Modules.Identity.Domain;

namespace Starter.Modules.Identity.Application;

/// <summary>The accounts store.</summary>
public interface IUserAccountRepository
{
    /// <summary>The account with this user name (compared in normalized form), or null.</summary>
    Task<UserAccount?> FindByUserNameAsync(string userName, CancellationToken cancellationToken);

    /// <summary>The account with this internal id, or null.</summary>
    Task<UserAccount?> FindByIdAsync(long userId, CancellationToken cancellationToken);

    /// <summary>The account with this token subject, or null.</summary>
    Task<UserAccount?> FindBySubjectAsync(Guid subjectId, CancellationToken cancellationToken);

    /// <summary>Stages a new account; <c>IIdentityUnitOfWork.SaveChangesAsync</c> writes it and assigns its id.</summary>
    void Add(UserAccount account);
}

/// <summary>The sessions store.</summary>
public interface IUserSessionRepository
{
    /// <summary>The session with this session id, or null.</summary>
    Task<UserSession?> FindBySessionIdAsync(string sessionId, CancellationToken cancellationToken);

    /// <summary>Stages a new session.</summary>
    void Add(UserSession session);
}

/// <summary>The Identity module's unit of work (implemented by the module persistence context).</summary>
public interface IIdentityUnitOfWork : IUnitOfWork;

/// <summary>The result of a password check.</summary>
public enum PasswordVerification
{
    /// <summary>The password does not match (or there was no account to match).</summary>
    Failed = 1,

    /// <summary>The password matches.</summary>
    Succeeded = 2,

    /// <summary>The password matches, and the hash uses outdated parameters: store a new hash.</summary>
    SucceededRehashNeeded = 3,
}

/// <summary>Password hashing (PBKDF2 through the ASP.NET Core Identity hasher).</summary>
public interface IPasswordHasher
{
    /// <summary>A new salted hash of <paramref name="password"/>.</summary>
    string Hash(string password);

    /// <summary>
    /// Checks <paramref name="password"/> against <paramref name="passwordHash"/>. With a null hash (no such account)
    /// it still does the work of a real check against an internal decoy and answers <see cref="PasswordVerification.Failed"/>,
    /// so the response time does not reveal whether a user name exists.
    /// </summary>
    PasswordVerification Verify(string? passwordHash, string password);
}

/// <summary>What an access token states: identity and session only, never permissions.</summary>
/// <param name="SubjectId">The user's token subject (<c>sub</c>).</param>
/// <param name="SessionId">The session id (<c>sid</c>).</param>
/// <param name="SessionVersion">The user's session version (<c>sver</c>).</param>
/// <param name="IssuedAt">The issue time.</param>
/// <param name="ExpiresAt">The expiry.</param>
public sealed record AccessTokenClaims(Guid SubjectId, string SessionId, long SessionVersion, DateTimeOffset IssuedAt, DateTimeOffset ExpiresAt);

/// <summary>Signs access tokens.</summary>
public interface IAccessTokenIssuer
{
    /// <summary>A signed access token for <paramref name="claims"/>.</summary>
    string Issue(AccessTokenClaims claims);
}

/// <summary>Cryptographically random session ids and refresh secrets.</summary>
public interface ISecureTokenGenerator
{
    /// <summary>A new session id (<see cref="IdentityLimits.SessionIdLength"/> lower-case hex characters).</summary>
    string NewSessionId();

    /// <summary>A new refresh secret (base64url of <see cref="IdentityLimits.RefreshSecretBytes"/> random bytes).</summary>
    string NewRefreshSecret();
}

/// <summary>The per-session revocation list every authenticated request is checked against.</summary>
public interface ISessionRevocation
{
    /// <summary>Revokes <paramref name="sessionId"/> until <paramref name="until"/> (when its last access token expires).</summary>
    Task RevokeAsync(string sessionId, DateTimeOffset until, CancellationToken cancellationToken);
}

/// <summary>The per-user session version (bumped by password change, deactivation or permission change).</summary>
public interface ISessionVersions
{
    /// <summary>The current version of the user (0 when never bumped).</summary>
    Task<long> CurrentAsync(Guid subjectId, CancellationToken cancellationToken);

    /// <summary>Moves the user's version on (atomic), so every session and access token issued before it is refused.</summary>
    Task<long> BumpAsync(Guid subjectId, CancellationToken cancellationToken);
}

/// <summary>The session lifetimes (from <c>Identity:Session</c>).</summary>
/// <param name="AccessTokenLifetime">How long an access token is valid.</param>
/// <param name="RefreshTokenLifetime">The absolute lifetime of a session (a refresh never extends it).</param>
/// <param name="IdleTimeout">How long a session may go without a refresh; the client also signs out after this much inactivity.</param>
public sealed record SessionSettings(TimeSpan AccessTokenLifetime, TimeSpan RefreshTokenLifetime, TimeSpan IdleTimeout);
