namespace Starter.Modules.Identity.Application;

/// <summary>
/// The Identity module's error codes. The client renders <c>errors.&lt;CODE&gt;</c> from its ar and en bundles.
/// </summary>
public static class IdentityErrorCodes
{
    /// <summary>
    /// The user name and password do not match an active account (400). One code for every reason (unknown user,
    /// wrong password, inactive account), so a caller cannot tell which.
    /// </summary>
    public const string InvalidCredentials = "IDENTITY_INVALID_CREDENTIALS";

    /// <summary>
    /// The refresh token cannot be used (400): malformed, unknown, expired, idle too long, already rotated, revoked,
    /// or the account's session version moved on. The client signs in again.
    /// </summary>
    public const string RefreshRejected = "IDENTITY_REFRESH_REJECTED";

    /// <summary>The access token carries no session id, so there is no session to end (403).</summary>
    public const string SessionRequired = "IDENTITY_SESSION_REQUIRED";
}
