namespace Starter.Modules.Identity.Domain;

/// <summary>What an auth event records. Stored as the integer value; never renumber a member.</summary>
public enum AuthEventType
{
    /// <summary>Credentials accepted and a session started (any sign-in mode).</summary>
    LoginSucceeded = 1,

    /// <summary>A sign-in that did not start a session; <see cref="AuthEvent.FailureReason"/> says why.</summary>
    LoginFailed = 2,

    /// <summary>The failed sign-in that crossed the lockout threshold (recorded in addition to its LoginFailed).</summary>
    AccountLockedOut = 3,

    /// <summary>A session was ended at the caller's request.</summary>
    Logout = 4,

    /// <summary>A refresh token was refused; <see cref="AuthEvent.FailureReason"/> says why.</summary>
    RefreshRejected = 5,

    /// <summary>An activation link was redeemed and the account's password set.</summary>
    AccountActivated = 6,

    /// <summary>An activation link was refused (malformed; expired; used; superseded or not for this account).</summary>
    ActivationRejected = 7,

    /// <summary>An administrator issued an activation link (a new account or a password reset).</summary>
    ActivationIssued = 8,

    /// <summary>An administrator deactivated the account.</summary>
    AccountDeactivated = 9,

    /// <summary>An administrator reactivated the account.</summary>
    AccountReactivated = 10,

    /// <summary>An administrator ended a lockout.</summary>
    AccountUnlocked = 11,
}

/// <summary>Why an auth event's decision went against the caller. Stored as the integer value; never renumber a member.</summary>
public enum AuthFailureReason
{
    /// <summary>No account matched the identifier.</summary>
    UnknownAccount = 1,

    /// <summary>The account exists; the password did not match.</summary>
    WrongPassword = 2,

    /// <summary>The account is deactivated.</summary>
    AccountInactive = 3,

    /// <summary>The account is locked out; the attempt was not counted.</summary>
    LockedOut = 4,

    /// <summary>The account is bound to another sign-in mode.</summary>
    BindingMismatch = 5,

    /// <summary>The password account has not redeemed its activation link (no password is set).</summary>
    PasswordNotSet = 6,

    /// <summary>The refresh token was malformed or named no session.</summary>
    RefreshTokenInvalid = 7,

    /// <summary>The session was already ended.</summary>
    SessionEnded = 8,

    /// <summary>The session passed its absolute lifetime.</summary>
    SessionExpired = 9,

    /// <summary>The session was idle longer than the idle timeout.</summary>
    SessionIdle = 10,

    /// <summary>An already rotated refresh token was presented again (treated as theft: the session ended).</summary>
    RefreshTokenReused = 11,

    /// <summary>The user's session version moved on, or the account was deactivated.</summary>
    SessionVersionStale = 12,

    /// <summary>The activation ticket was malformed; expired; already used; superseded or not for this account.</summary>
    ActivationTicketRejected = 13,

    /// <summary>Basic login is off (<c>Auth:Modes</c>) and the account is not the break-glass administrator.</summary>
    LoginModeDisabled = 14,
}

/// <summary>
/// One row of the authentication trail (<c>identity.AuthEvents</c>): append-only, never read back by the
/// application. It holds no password, token, ticket or raw user name: the identifier the caller typed is kept only as
/// a keyed pseudonymous hash, and the user only as the internal id. No foreign key, so a row outlives its account and
/// a failed sign-in for an unknown user is recorded too.
/// </summary>
public sealed class AuthEvent
{
    private AuthEvent()
    {
    }

    /// <summary>The internal id.</summary>
    public long Id { get; private set; }

    /// <summary>What happened.</summary>
    public AuthEventType EventType { get; private set; }

    /// <summary>Why it went against the caller, or null for a success.</summary>
    public AuthFailureReason? FailureReason { get; private set; }

    /// <summary>The internal id of the account the event resolved to, or null (unknown user).</summary>
    public long? UserId { get; private set; }

    /// <summary>The keyed hash of the identifier the caller typed, or null.</summary>
    public string? UserNameHash { get; private set; }

    /// <summary>The sign-in mode involved, or null for an event outside a sign-in (administration, activation).</summary>
    public SignInMode? SignInMode { get; private set; }

    /// <summary>When the event happened.</summary>
    public DateTimeOffset OccurredAt { get; private set; }

    /// <summary>The request correlation id, or null outside a request.</summary>
    public string? CorrelationId { get; private set; }

    /// <summary>A new row; text longer than its column is cut, never refused (a lost row is an attacker's win).</summary>
    public static AuthEvent Record(NewAuthEvent authEvent)
    {
        ArgumentNullException.ThrowIfNull(authEvent);
        return new AuthEvent
        {
            EventType = authEvent.EventType,
            FailureReason = authEvent.FailureReason,
            UserId = authEvent.UserId,
            UserNameHash = Fit(authEvent.UserNameHash, IdentityLimits.UserNameHashMaxLength),
            SignInMode = authEvent.SignInMode,
            OccurredAt = authEvent.OccurredAt,
            CorrelationId = Fit(authEvent.CorrelationId, IdentityLimits.CorrelationIdMaxLength),
        };
    }

    private static string? Fit(string? value, int maxLength) =>
        value is null || value.Length <= maxLength ? value : value[..maxLength];
}

/// <summary>What recording an auth event row needs.</summary>
/// <param name="EventType">What happened.</param>
/// <param name="FailureReason">Why it went against the caller, or null.</param>
/// <param name="UserId">The account's internal id, or null.</param>
/// <param name="UserNameHash">The keyed hash of the typed identifier, or null.</param>
/// <param name="SignInMode">The sign-in mode, or null.</param>
/// <param name="OccurredAt">When it happened.</param>
/// <param name="CorrelationId">The request correlation id, or null.</param>
public sealed record NewAuthEvent(
    AuthEventType EventType,
    AuthFailureReason? FailureReason,
    long? UserId,
    string? UserNameHash,
    SignInMode? SignInMode,
    DateTimeOffset OccurredAt,
    string? CorrelationId);
