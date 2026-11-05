using System.Security.Cryptography;

namespace Starter.Modules.Identity.Domain;

/// <summary>Why a refresh is refused, or that it is allowed.</summary>
public enum RefreshDecision
{
    /// <summary>The session may be refreshed.</summary>
    Allowed = 1,

    /// <summary>The session was ended (logout, or an earlier refusal that revoked it).</summary>
    Revoked = 2,

    /// <summary>The session passed its absolute lifetime.</summary>
    Expired = 3,

    /// <summary>The session was idle longer than the idle timeout.</summary>
    Idle = 4,

    /// <summary>The presented secret is not the current one: an old (already rotated) or forged refresh token.</summary>
    SecretMismatch = 5,
}

/// <summary>
/// One signed-in session: its id (the token <c>sid</c>), the current refresh secret (stored as a SHA-256 hash only),
/// the user's session version it was issued under, its absolute expiry and its last activity. A refresh rotates the
/// secret; presenting an old secret is treated as theft and ends the session.
/// </summary>
public sealed class UserSession
{
    private UserSession()
    {
        SessionId = string.Empty;
        RefreshSecretHash = [];
    }

    /// <summary>The internal id.</summary>
    public long Id { get; private set; }

    /// <summary>The session id carried by every token of the session (<c>sid</c>).</summary>
    public string SessionId { get; private set; }

    /// <summary>The internal id of the user.</summary>
    public long UserId { get; private set; }

    /// <summary>SHA-256 of the current refresh secret.</summary>
    public byte[] RefreshSecretHash { get; private set; }

    /// <summary>How many times the refresh secret was rotated.</summary>
    public int RefreshGeneration { get; private set; }

    /// <summary>The user's session version when the session started (a later bump ends the session).</summary>
    public long SessionVersion { get; private set; }

    /// <summary>When the session started.</summary>
    public DateTimeOffset CreatedAt { get; private set; }

    /// <summary>When the session was last started or refreshed (the idle timeout counts from here).</summary>
    public DateTimeOffset LastActivityAt { get; private set; }

    /// <summary>The absolute end of the session; no refresh extends it.</summary>
    public DateTimeOffset ExpiresAt { get; private set; }

    /// <summary>When the session was ended, or null while it is open.</summary>
    public DateTimeOffset? RevokedAt { get; private set; }

    /// <summary>How the session was signed in.</summary>
    public SignInMode SignInMode { get; private set; }

    /// <summary>
    /// The identity provider's id_token, kept only as the <c>id_token_hint</c> of the provider's end-session call
    /// (SSO sessions), or null. A hint longer than <see cref="IdentityLimits.IdTokenHintMaxLength"/> is not kept (the
    /// provider then asks the user to confirm the logout).
    /// </summary>
    public string? IdTokenHint { get; private set; }

    /// <summary>Starts a session.</summary>
    public static UserSession Start(NewUserSession session)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentException.ThrowIfNullOrWhiteSpace(session.SessionId);
        return new UserSession
        {
            SessionId = session.SessionId,
            UserId = session.UserId,
            RefreshSecretHash = HashSecret(session.RefreshSecret),
            SessionVersion = session.SessionVersion,
            CreatedAt = session.StartedAt,
            LastActivityAt = session.StartedAt,
            ExpiresAt = session.StartedAt + session.Lifetime,
            SignInMode = session.Mode,
            IdTokenHint = session.IdTokenHint is { Length: <= IdentityLimits.IdTokenHintMaxLength } hint ? hint : null,
        };
    }

    /// <summary>Whether <paramref name="presentedSecret"/> may refresh this session at <paramref name="now"/>.</summary>
    public RefreshDecision CheckRefresh(string presentedSecret, DateTimeOffset now, TimeSpan idleTimeout)
    {
        if (RevokedAt is not null)
        {
            return RefreshDecision.Revoked;
        }

        if (now >= ExpiresAt)
        {
            return RefreshDecision.Expired;
        }

        if (now - LastActivityAt > idleTimeout)
        {
            return RefreshDecision.Idle;
        }

        return CryptographicOperations.FixedTimeEquals(HashSecret(presentedSecret), RefreshSecretHash)
            ? RefreshDecision.Allowed
            : RefreshDecision.SecretMismatch;
    }

    /// <summary>Replaces the refresh secret (the old one stops working) and records the activity.</summary>
    public void Rotate(string newSecret, DateTimeOffset now)
    {
        RefreshSecretHash = HashSecret(newSecret);
        RefreshGeneration++;
        LastActivityAt = now;
    }

    /// <summary>Ends the session (idempotent: the first end time is kept).</summary>
    public void Revoke(DateTimeOffset now) => RevokedAt ??= now;

    private static byte[] HashSecret(string secret)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(secret);
        return SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(secret));
    }
}

/// <summary>What starting a session needs.</summary>
/// <param name="SessionId">A new random session id.</param>
/// <param name="UserId">The internal user id.</param>
/// <param name="RefreshSecret">The first refresh secret (only its hash is kept).</param>
/// <param name="SessionVersion">The user's current session version.</param>
/// <param name="StartedAt">The start time.</param>
/// <param name="Lifetime">The absolute session lifetime.</param>
/// <param name="Mode">How the session was signed in.</param>
/// <param name="IdTokenHint">The identity provider's id_token for its end-session call (SSO), or null.</param>
public sealed record NewUserSession(
    string SessionId,
    long UserId,
    string RefreshSecret,
    long SessionVersion,
    DateTimeOffset StartedAt,
    TimeSpan Lifetime,
    SignInMode Mode,
    string? IdTokenHint);
