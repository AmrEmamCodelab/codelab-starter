using Codelab.Security.PublicIds;
using Starter.Application.Users;
using Starter.Application.Validation;
using Starter.Domain.Users;
using Starter.Modules.Users.Domain;

namespace Starter.Modules.Users.Application.Accounts;

/// <summary>
/// The body of <c>POST /api/admin/users</c>. There is no password field: a password-bound account gets an activation
/// link, an Active Directory or SSO account signs in through its directory or identity provider.
/// </summary>
/// <param name="UserName">The sign-in name (unique).</param>
/// <param name="DisplayName">The name shown in the shell and the lists.</param>
/// <param name="Email">The e-mail address (unique when given), or null.</param>
/// <param name="LoginName">The directory login name (unique when given), or null.</param>
/// <param name="Upn">The user principal name (unique when given), or null.</param>
/// <param name="Kind">Internal (1) or external (2).</param>
/// <param name="Binding">Password (1), Active Directory (2) or SSO (3).</param>
/// <param name="OrganizationId">The organization; required for an external user.</param>
public sealed record CreateUserCommand(
    string? UserName,
    string? DisplayName,
    string? Email,
    string? LoginName,
    string? Upn,
    UserKind? Kind,
    AccountBinding? Binding,
    PublicId<OrganizationIds>? OrganizationId)
{
    /// <summary>The fields a create shares with an update.</summary>
    public AccountFields Fields => new(DisplayName, Email, LoginName, Upn, Kind, Binding, OrganizationId);
}

/// <summary>The body of <c>PUT /api/admin/users/{id}</c> (the user name never changes).</summary>
/// <param name="DisplayName">The name shown in the shell and the lists.</param>
/// <param name="Email">The e-mail address (unique when given), or null.</param>
/// <param name="LoginName">The directory login name (unique when given), or null.</param>
/// <param name="Upn">The user principal name (unique when given), or null.</param>
/// <param name="Kind">Internal (1) or external (2).</param>
/// <param name="Binding">Password (1), Active Directory (2) or SSO (3).</param>
/// <param name="OrganizationId">The organization; required for an external user.</param>
public sealed record UpdateUserCommand(
    string? DisplayName,
    string? Email,
    string? LoginName,
    string? Upn,
    UserKind? Kind,
    AccountBinding? Binding,
    PublicId<OrganizationIds>? OrganizationId)
{
    /// <summary>The fields of the update.</summary>
    public AccountFields Fields => new(DisplayName, Email, LoginName, Upn, Kind, Binding, OrganizationId);
}

/// <summary>The query of <c>GET /api/admin/users</c>; every value is optional.</summary>
/// <param name="Search">Matched against the user name, display name and e-mail address.</param>
/// <param name="OrganizationId">Only this organization's accounts.</param>
/// <param name="Kind">Only this kind.</param>
/// <param name="IsActive">Only active or deactivated accounts.</param>
/// <param name="Page">The 1-based page (default 1).</param>
/// <param name="PageSize">The page size (default <see cref="UsersLimits.DefaultPageSize"/>, at most <see cref="UsersLimits.MaxPageSize"/>).</param>
public sealed record UserListQuery(string? Search, PublicId<OrganizationIds>? OrganizationId, UserKind? Kind, bool? IsActive, int? Page, int? PageSize);

/// <summary>The account fields a create and an update share, as sent.</summary>
/// <param name="DisplayName">The display name.</param>
/// <param name="Email">The e-mail address, or null.</param>
/// <param name="LoginName">The directory login name, or null.</param>
/// <param name="Upn">The user principal name, or null.</param>
/// <param name="Kind">The kind.</param>
/// <param name="Binding">The binding.</param>
/// <param name="OrganizationId">The organization, or null.</param>
public sealed record AccountFields(
    string? DisplayName,
    string? Email,
    string? LoginName,
    string? Upn,
    UserKind? Kind,
    AccountBinding? Binding,
    PublicId<OrganizationIds>? OrganizationId)
{
    /// <summary>The organization's internal id, or null.</summary>
    public long? OrganizationValue => OrganizationId is { HasValue: true } id ? id.Value : null;

    /// <summary>The Identity changes of these (validated) fields: optional text trimmed, blank as null.</summary>
    public AccountChanges ToChanges() => new(
        DisplayName: DisplayName!.Trim(),
        Email: OptionalText.Clean(Email),
        LoginName: OptionalText.Clean(LoginName),
        Upn: OptionalText.Clean(Upn),
        Kind: Kind!.Value,
        Binding: Binding!.Value,
        OrganizationId: OrganizationValue);
}

/// <summary>Validates <see cref="CreateUserCommand"/>.</summary>
public sealed class CreateUserCommandValidator : IValidator<CreateUserCommand>
{
    /// <inheritdoc />
    public ValidationErrors Validate(CreateUserCommand input)
    {
        ArgumentNullException.ThrowIfNull(input);
        var errors = new ValidationErrors()
            .RequireText(FieldNames.Of(nameof(CreateUserCommand.UserName)), input.UserName, UsersLimits.UserNameMaxLength);
        return AccountFieldRules.Check(errors, input.Fields);
    }
}

/// <summary>Validates <see cref="UpdateUserCommand"/>.</summary>
public sealed class UpdateUserCommandValidator : IValidator<UpdateUserCommand>
{
    /// <inheritdoc />
    public ValidationErrors Validate(UpdateUserCommand input)
    {
        ArgumentNullException.ThrowIfNull(input);
        return AccountFieldRules.Check(new ValidationErrors(), input.Fields);
    }
}

/// <summary>The field rules of the account form (one owner for create and edit).</summary>
public static class AccountFieldRules
{
    /// <summary>
    /// Adds the field errors of <paramref name="fields"/>: a display name, a well-formed e-mail address when given,
    /// lengths within the Identity columns, a defined kind and binding, and an organization for an external user.
    /// </summary>
    public static ValidationErrors Check(ValidationErrors errors, AccountFields fields)
    {
        ArgumentNullException.ThrowIfNull(errors);
        ArgumentNullException.ThrowIfNull(fields);
        errors.RequireText(FieldNames.Of(nameof(AccountFields.DisplayName)), fields.DisplayName, UsersLimits.DisplayNameMaxLength);
        CheckEmail(errors, fields.Email);
        OptionalText.CheckLength(errors, FieldNames.Of(nameof(AccountFields.LoginName)), fields.LoginName, UsersLimits.LoginNameMaxLength);
        OptionalText.CheckLength(errors, FieldNames.Of(nameof(AccountFields.Upn)), fields.Upn, UsersLimits.UpnMaxLength);
        EnumFields.Require(errors, FieldNames.Of(nameof(AccountFields.Kind)), fields.Kind);
        EnumFields.Require(errors, FieldNames.Of(nameof(AccountFields.Binding)), fields.Binding);
        if (fields.Kind == UserKind.External && fields.OrganizationValue is null)
        {
            errors.Add(FieldNames.Of(nameof(AccountFields.OrganizationId)), ValidationMessages.Required);
        }

        return errors;
    }

    private static void CheckEmail(ValidationErrors errors, string? email)
    {
        var field = FieldNames.Of(nameof(AccountFields.Email));
        var clean = OptionalText.Clean(email);
        if (clean is null)
        {
            return;
        }

        if (clean.Length > UsersLimits.EmailMaxLength)
        {
            errors.Add(field, ValidationMessages.TooLong);
        }
        else if (!EmailAddresses.IsWellFormed(clean))
        {
            errors.Add(field, ValidationMessages.InvalidFormat);
        }
    }
}

/// <summary>Optional text values: trimmed, blank as null.</summary>
public static class OptionalText
{
    /// <summary>The trimmed value, or null when it is null or blank.</summary>
    public static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    /// <summary>Adds <see cref="ValidationMessages.TooLong"/> when the trimmed value is longer than <paramref name="maxLength"/>.</summary>
    public static void CheckLength(ValidationErrors errors, string field, string? value, int maxLength)
    {
        ArgumentNullException.ThrowIfNull(errors);
        if (Clean(value) is { } clean && clean.Length > maxLength)
        {
            errors.Add(field, ValidationMessages.TooLong);
        }
    }
}
