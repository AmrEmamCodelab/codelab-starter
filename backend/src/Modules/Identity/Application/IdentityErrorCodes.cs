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

    /// <summary>
    /// The activation link cannot be used (400): malformed, expired, already used, superseded by a newer link, or the
    /// account is deactivated or no longer password bound. One code for every reason.
    /// </summary>
    public const string ActivationRejected = "IDENTITY_ACTIVATION_REJECTED";

    /// <summary>No account has this id (404).</summary>
    public const string AccountNotFound = "IDENTITY_ACCOUNT_NOT_FOUND";

    /// <summary>Another account already has this user name (409).</summary>
    public const string UserNameTaken = "IDENTITY_USER_NAME_TAKEN";

    /// <summary>Another account already has this e-mail address (409).</summary>
    public const string EmailTaken = "IDENTITY_EMAIL_TAKEN";

    /// <summary>Another account already has this directory login name (409).</summary>
    public const string LoginNameTaken = "IDENTITY_LOGIN_NAME_TAKEN";

    /// <summary>Another account already has this user principal name (409).</summary>
    public const string UpnTaken = "IDENTITY_UPN_TAKEN";

    /// <summary>
    /// The break-glass administrator is configured, not administered (409): it cannot be edited, deactivated or given
    /// an activation link here; its configuration is the authority.
    /// </summary>
    public const string BreakGlassProtected = "IDENTITY_BREAK_GLASS_PROTECTED";

    /// <summary>The account is bound to Active Directory or SSO, so it has no password to activate or reset (422).</summary>
    public const string ActivationNotApplicable = "IDENTITY_ACTIVATION_NOT_APPLICABLE";

    /// <summary>The account is deactivated (422): reactivate it before issuing an activation link.</summary>
    public const string AccountInactive = "IDENTITY_ACCOUNT_INACTIVE";

    /// <summary>
    /// Activation links cannot be issued because sealed tickets are switched off in this deployment (422;
    /// <c>Codelab:Security:Tickets:Enabled</c>).
    /// </summary>
    public const string ActivationUnavailable = "IDENTITY_ACTIVATION_UNAVAILABLE";
}

/// <summary>
/// The Identity module's per-field validation message codes, beside the kernel's <c>ValidationMessages</c>. The client
/// renders <c>validation.&lt;CODE&gt;</c> from its ar and en bundles.
/// </summary>
public static class IdentityValidationMessages
{
    /// <summary>The password is shorter than the configured minimum (<c>Identity:Password:MinLength</c>).</summary>
    public const string PasswordTooShort = "PASSWORD_TOO_SHORT";

    /// <summary>The password has fewer distinct characters than the configured minimum (<c>Identity:Password:MinDistinctCharacters</c>).</summary>
    public const string PasswordTooSimple = "PASSWORD_TOO_SIMPLE";

    /// <summary>The value is not one of the enumeration's defined members.</summary>
    public const string UnknownValue = "UNKNOWN_VALUE";
}
