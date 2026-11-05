using Starter.Application.Authorization;
using Starter.Application.Validation;
using Starter.Modules.Access.Domain;

namespace Starter.Modules.Access.Application;

/// <summary>The body of <c>POST /api/admin/access/roles</c>, validated by <see cref="CreateRoleValidator"/>.</summary>
/// <param name="Code">The stable code (lower-case kebab, <see cref="RoleCodeFormat"/>); it never changes.</param>
/// <param name="NameEn">The English name (unique).</param>
/// <param name="NameAr">The Arabic name (unique).</param>
/// <param name="Permissions">The permission keys the role grants (each in the live catalog).</param>
public sealed record CreateRoleCommand(string Code, string NameEn, string NameAr, IReadOnlyList<string> Permissions);

/// <summary>The body of <c>PUT /api/admin/access/roles/{id}</c>, validated by <see cref="UpdateRoleValidator"/>.</summary>
/// <param name="NameEn">The English name (unique).</param>
/// <param name="NameAr">The Arabic name (unique).</param>
/// <param name="Permissions">The permission keys the role grants after the write (replaces the set).</param>
/// <param name="IsActive">False deactivates the role (it then grants nothing; assignments stay).</param>
/// <param name="ConfirmBreakGlassOnly">
/// True accepts that the write leaves only the break-glass administrator able to manage access (otherwise refused
/// with <see cref="AccessErrorCodes.LastManager"/>).
/// </param>
public sealed record UpdateRoleCommand(
    string NameEn,
    string NameAr,
    IReadOnlyList<string> Permissions,
    bool IsActive,
    bool ConfirmBreakGlassOnly = false);

/// <summary>The shared field checks of the role bodies (one owner for names and permission lists).</summary>
internal static class RoleFieldRules
{
    public static ValidationErrors Names(this ValidationErrors errors, string field, string? value) =>
        errors.RequireText(field: field, value: value, maxLength: AccessLimits.RoleNameMaxLength);

    public static ValidationErrors PermissionKeys(this ValidationErrors errors, string field, IReadOnlyList<string>? keys)
    {
        if (keys is null)
        {
            return errors.Add(field: field, message: ValidationMessages.Required);
        }

        if (keys.Count > AccessLimits.PermissionsPerWriteMax)
        {
            return errors.Add(field: field, message: ValidationMessages.TooLong);
        }

        return keys.Any(key => !PermissionKeyFormat.IsValid(key))
            ? errors.Add(field: field, message: ValidationMessages.InvalidFormat)
            : errors;
    }
}

/// <summary>Validates <see cref="CreateRoleCommand"/>: every field error at once, never throws.</summary>
public sealed class CreateRoleValidator : IValidator<CreateRoleCommand>
{
    /// <inheritdoc />
    public ValidationErrors Validate(CreateRoleCommand input)
    {
        ArgumentNullException.ThrowIfNull(input);
        var codeField = FieldNames.Of(nameof(CreateRoleCommand.Code));
        var errors = new ValidationErrors().RequireText(field: codeField, value: input.Code, maxLength: AccessLimits.RoleCodeMaxLength);
        if (!string.IsNullOrWhiteSpace(input.Code) && input.Code.Length <= AccessLimits.RoleCodeMaxLength && !RoleCodeFormat.IsValid(input.Code))
        {
            errors.Add(field: codeField, message: ValidationMessages.InvalidFormat);
        }

        return errors
            .Names(field: FieldNames.Of(nameof(CreateRoleCommand.NameEn)), value: input.NameEn)
            .Names(field: FieldNames.Of(nameof(CreateRoleCommand.NameAr)), value: input.NameAr)
            .PermissionKeys(FieldNames.Of(nameof(CreateRoleCommand.Permissions)), input.Permissions);
    }
}

/// <summary>Validates <see cref="UpdateRoleCommand"/>: every field error at once, never throws.</summary>
public sealed class UpdateRoleValidator : IValidator<UpdateRoleCommand>
{
    /// <inheritdoc />
    public ValidationErrors Validate(UpdateRoleCommand input)
    {
        ArgumentNullException.ThrowIfNull(input);
        return new ValidationErrors()
            .Names(field: FieldNames.Of(nameof(UpdateRoleCommand.NameEn)), value: input.NameEn)
            .Names(field: FieldNames.Of(nameof(UpdateRoleCommand.NameAr)), value: input.NameAr)
            .PermissionKeys(FieldNames.Of(nameof(UpdateRoleCommand.Permissions)), input.Permissions);
    }
}
