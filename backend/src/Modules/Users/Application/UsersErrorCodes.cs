namespace Starter.Modules.Users.Application;

/// <summary>
/// The Users module's error codes. The client renders <c>errors.&lt;CODE&gt;</c> from its ar and en bundles. Account
/// refusals that come from the Identity module (duplicate user name, e-mail address, ...) pass through with Identity's
/// own codes.
/// </summary>
public static class UsersErrorCodes
{
    /// <summary>No account has this id (404).</summary>
    public const string UserNotFound = "USERS_USER_NOT_FOUND";

    /// <summary>No organization has this id, or the account names an organization that does not exist (404 / 422).</summary>
    public const string OrganizationNotFound = "USERS_ORGANIZATION_NOT_FOUND";

    /// <summary>The organization is deactivated and accepts no new member (422).</summary>
    public const string OrganizationInactive = "USERS_ORGANIZATION_INACTIVE";

    /// <summary>The account's kind does not match the organization's kind (external users need an external organization) (422).</summary>
    public const string OrganizationKindMismatch = "USERS_ORGANIZATION_KIND_MISMATCH";

    /// <summary>Another organization already has this code (409).</summary>
    public const string OrganizationCodeTaken = "USERS_ORGANIZATION_CODE_TAKEN";

    /// <summary>The organization's kind cannot change while accounts belong to it (409).</summary>
    public const string OrganizationKindInUse = "USERS_ORGANIZATION_KIND_IN_USE";

    /// <summary>The break-glass administrator is configured, not administered here (409).</summary>
    public const string BreakGlassProtected = "USERS_BREAK_GLASS_PROTECTED";

    /// <summary>An administrator cannot deactivate their own account (409).</summary>
    public const string CannotDeactivateSelf = "USERS_CANNOT_DEACTIVATE_SELF";

    /// <summary>Users:Activation:LinkBaseUrl is not configured, so no activation link can be issued (422).</summary>
    public const string ActivationLinkUnconfigured = "USERS_ACTIVATION_LINK_UNCONFIGURED";

    /// <summary>Only a password-bound account has an activation; Active Directory and SSO accounts sign in elsewhere (422).</summary>
    public const string ActivationNotApplicable = "USERS_ACTIVATION_NOT_APPLICABLE";

    /// <summary>The import request carries no file (400).</summary>
    public const string ImportFileRequired = "USERS_IMPORT_FILE_REQUIRED";

    /// <summary>The file is not a readable .xlsx workbook (400).</summary>
    public const string ImportFileInvalid = "USERS_IMPORT_FILE_INVALID";

    /// <summary>The file, or the workbook inside it, is larger than the import allows (400).</summary>
    public const string ImportFileTooLarge = "USERS_IMPORT_FILE_TOO_LARGE";

    /// <summary>The sheet has more data rows than the import allows (400).</summary>
    public const string ImportTooManyRows = "USERS_IMPORT_TOO_MANY_ROWS";

    /// <summary>The sheet has no data row (400).</summary>
    public const string ImportNoRows = "USERS_IMPORT_NO_ROWS";

    /// <summary>The header row lacks a required column; the field errors name each missing column (400).</summary>
    public const string ImportColumnsMissing = "USERS_IMPORT_COLUMNS_MISSING";

    /// <summary>No import batch has this id for the caller (404).</summary>
    public const string ImportBatchNotFound = "USERS_IMPORT_BATCH_NOT_FOUND";

    /// <summary>The import batch expired before it was committed; preview the file again (409).</summary>
    public const string ImportBatchExpired = "USERS_IMPORT_BATCH_EXPIRED";

    /// <summary>The import batch was already committed (409).</summary>
    public const string ImportBatchCommitted = "USERS_IMPORT_BATCH_COMMITTED";

    /// <summary>
    /// The organization is outside the caller's organization scope: an own-organization holder can only place accounts
    /// in, and administer, their own organization (403).
    /// </summary>
    public const string OrganizationOutOfScope = "USERS_ORGANIZATION_OUT_OF_SCOPE";

    /// <summary>
    /// The account holds a permission the caller does not hold (or holds it more widely), so the caller may not reset,
    /// edit or switch it off (403).
    /// </summary>
    public const string TargetOutranksCaller = "USERS_TARGET_OUTRANKS_CALLER";

    /// <summary>An administrator cannot change their own kind, binding or organization (409).</summary>
    public const string CannotChangeOwnAccess = "USERS_CANNOT_CHANGE_OWN_ACCESS";

    /// <summary>Deactivating the account would leave no access manager other than break-glass (409).</summary>
    public const string LastAccessManager = "USERS_LAST_ACCESS_MANAGER";

    // scaffold:error-codes
}
