namespace Starter.Modules.Identity.Application.LoginModes;

/// <summary>
/// The error codes of the switchable login modes (basic, Active Directory, SSO). The client renders
/// <c>errors.&lt;CODE&gt;</c> from its ar and en bundles. A refused directory or SSO identity answers the module's
/// <see cref="IdentityErrorCodes.InvalidCredentials"/>, never one of these, so a caller cannot tell why.
/// </summary>
public static class LoginModeErrorCodes
{
    /// <summary>The endpoint belongs to a login mode this deployment has not switched on (<c>Auth:Modes</c>; 404).</summary>
    public const string LoginModeDisabled = "IDENTITY_LOGIN_MODE_DISABLED";

    /// <summary>The Active Directory could not be asked (unreachable, timed out, TLS refused); no password was checked (503).</summary>
    public const string DirectoryUnavailable = "IDENTITY_DIRECTORY_UNAVAILABLE";

    /// <summary>
    /// The SSO callback cannot be completed (fragment <c>#error=</c>): the provider answered an error, the sign-in
    /// cookie is missing, expired or already used, the state does not match, or the id_token is invalid.
    /// </summary>
    public const string SsoRejected = "IDENTITY_SSO_REJECTED";

    /// <summary>The identity provider, or the one-time ticket store, could not be asked (fragment <c>#error=</c>).</summary>
    public const string SsoUnavailable = "IDENTITY_SSO_UNAVAILABLE";

    /// <summary>The SSO exchange ticket cannot be used (400): malformed, expired, already used, or not this browser's.</summary>
    public const string SsoTicketRejected = "IDENTITY_SSO_TICKET_REJECTED";
}
