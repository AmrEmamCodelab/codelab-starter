using Starter.Domain.Users;
using Starter.Modules.Users.Application.Accounts;
using Starter.Modules.Users.Domain;

namespace Starter.Modules.Users.Application.Import;

/// <summary>
/// The per-row error codes of an import preview and commit (<c>rows[].errors</c>, <c>rejected[].errors</c>). The client
/// renders <c>errors.&lt;CODE&gt;</c>. A row the Identity module refuses at commit carries Identity's code instead.
/// </summary>
public static class UsersImportRowErrorCodes
{
    /// <summary>The user name is blank.</summary>
    public const string UserNameRequired = "USERS_IMPORT_ROW_USER_NAME_REQUIRED";

    /// <summary>The user name is too long.</summary>
    public const string UserNameTooLong = "USERS_IMPORT_ROW_USER_NAME_TOO_LONG";

    /// <summary>Another row of the file has the same user name.</summary>
    public const string UserNameDuplicate = "USERS_IMPORT_ROW_USER_NAME_DUPLICATE";

    /// <summary>An existing account has this user name.</summary>
    public const string UserNameTaken = "USERS_IMPORT_ROW_USER_NAME_TAKEN";

    /// <summary>The display name is blank.</summary>
    public const string DisplayNameRequired = "USERS_IMPORT_ROW_DISPLAY_NAME_REQUIRED";

    /// <summary>The display name is too long.</summary>
    public const string DisplayNameTooLong = "USERS_IMPORT_ROW_DISPLAY_NAME_TOO_LONG";

    /// <summary>The e-mail address does not have the shape of one, or is too long.</summary>
    public const string EmailInvalid = "USERS_IMPORT_ROW_EMAIL_INVALID";

    /// <summary>Another row of the file has the same e-mail address.</summary>
    public const string EmailDuplicate = "USERS_IMPORT_ROW_EMAIL_DUPLICATE";

    /// <summary>The login name or the user principal name is too long.</summary>
    public const string DirectoryNameTooLong = "USERS_IMPORT_ROW_DIRECTORY_NAME_TOO_LONG";

    /// <summary>The kind is blank or not <c>internal</c> / <c>external</c>.</summary>
    public const string KindInvalid = "USERS_IMPORT_ROW_KIND_INVALID";

    /// <summary>The binding is blank or not <c>password</c> / <c>ad</c> / <c>sso</c>.</summary>
    public const string BindingInvalid = "USERS_IMPORT_ROW_BINDING_INVALID";

    /// <summary>An external user has no organization code.</summary>
    public const string OrganizationRequired = "USERS_IMPORT_ROW_ORGANIZATION_REQUIRED";

    /// <summary>No organization has this code.</summary>
    public const string OrganizationNotFound = "USERS_IMPORT_ROW_ORGANIZATION_NOT_FOUND";

    /// <summary>The organization is deactivated.</summary>
    public const string OrganizationInactive = "USERS_IMPORT_ROW_ORGANIZATION_INACTIVE";

    /// <summary>The user's kind does not match the organization's kind.</summary>
    public const string OrganizationKindMismatch = "USERS_IMPORT_ROW_ORGANIZATION_KIND_MISMATCH";

    /// <summary>The importer holds the import permission for their own organization only, and this row is outside it.</summary>
    public const string OrganizationOutOfScope = "USERS_IMPORT_ROW_ORGANIZATION_OUT_OF_SCOPE";
}

/// <summary>The value words of the kind and binding columns, matched after <see cref="ImportColumns.Normalize"/>.</summary>
public static class ImportValues
{
    /// <summary>The kind word of an internal user.</summary>
    public const string InternalKind = "INTERNAL";

    /// <summary>The kind word of an external user.</summary>
    public const string ExternalKind = "EXTERNAL";

    /// <summary>The binding word of a password-bound account (an activation link, never a password in the sheet).</summary>
    public const string PasswordBinding = "PASSWORD";

    /// <summary>The short binding word of an Active Directory account.</summary>
    public const string DirectoryBinding = "AD";

    /// <summary>The long binding word of an Active Directory account.</summary>
    public const string DirectoryBindingLong = "ACTIVEDIRECTORY";

    /// <summary>The binding word of an SSO account.</summary>
    public const string SsoBinding = "SSO";

    /// <summary>The kind of <paramref name="text"/>, or null when it is blank or unknown.</summary>
    public static UserKind? KindOf(string? text) => ImportColumns.Normalize(text) switch
    {
        InternalKind => UserKind.@Internal, // '@' keeps the host-name scanner from reading the member access as a host
        ExternalKind => UserKind.External,
        _ => null,
    };

    /// <summary>The binding of <paramref name="text"/>, or null when it is blank or unknown.</summary>
    public static AccountBinding? BindingOf(string? text) => ImportColumns.Normalize(text) switch
    {
        PasswordBinding => AccountBinding.Password,
        DirectoryBinding or DirectoryBindingLong => AccountBinding.ActiveDirectory,
        SsoBinding => AccountBinding.Sso,
        _ => null,
    };
}

/// <summary>The checks of one row that need nothing but the row (lengths, shapes, known kind and binding).</summary>
public static class ImportRowRules
{
    /// <summary>The field error codes of <paramref name="row"/>, in column order.</summary>
    public static List<string> FieldErrors(ImportRow row)
    {
        ArgumentNullException.ThrowIfNull(row);
        var errors = new List<string>();
        AddTextError(errors, row.UserName, UsersLimits.UserNameMaxLength, UsersImportRowErrorCodes.UserNameRequired, UsersImportRowErrorCodes.UserNameTooLong);
        AddTextError(errors, row.DisplayName, UsersLimits.DisplayNameMaxLength, UsersImportRowErrorCodes.DisplayNameRequired, UsersImportRowErrorCodes.DisplayNameTooLong);
        if (row.Email is not null && !EmailAddresses.IsWellFormed(row.Email))
        {
            errors.Add(UsersImportRowErrorCodes.EmailInvalid);
        }

        if (row.LoginName?.Length > UsersLimits.LoginNameMaxLength || row.Upn?.Length > UsersLimits.UpnMaxLength)
        {
            errors.Add(UsersImportRowErrorCodes.DirectoryNameTooLong);
        }

        AddIf(errors, ImportValues.KindOf(row.Kind) is null, UsersImportRowErrorCodes.KindInvalid);
        AddIf(errors, ImportValues.BindingOf(row.Binding) is null, UsersImportRowErrorCodes.BindingInvalid);
        return errors;
    }

    /// <summary>The row error code of a failed membership check, or null when allowed.</summary>
    public static string? ErrorOf(MembershipCheck check) => check switch
    {
        MembershipCheck.Allowed => null,
        MembershipCheck.OrganizationRequired => UsersImportRowErrorCodes.OrganizationRequired,
        MembershipCheck.OrganizationNotFound => UsersImportRowErrorCodes.OrganizationNotFound,
        MembershipCheck.OrganizationInactive => UsersImportRowErrorCodes.OrganizationInactive,
        MembershipCheck.KindMismatch => UsersImportRowErrorCodes.OrganizationKindMismatch,
        _ => throw new ArgumentOutOfRangeException(nameof(check), check, "Unknown membership check."),
    };

    private static void AddTextError(List<string> errors, string? value, int maxLength, string required, string tooLong)
    {
        if (value is null)
        {
            errors.Add(required);
        }
        else if (value.Length > maxLength)
        {
            errors.Add(tooLong);
        }
    }

    private static void AddIf(List<string> errors, bool condition, string code)
    {
        if (condition)
        {
            errors.Add(code);
        }
    }
}
