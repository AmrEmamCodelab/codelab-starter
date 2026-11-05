using Codelab.Security.PublicIds;
using Starter.Application.Authorization;
using Starter.Application.Results;
using Starter.Application.Validation;
using Starter.Modules.Access.Domain;

namespace Starter.Modules.Access.Application;

/// <summary>One role to assign.</summary>
/// <param name="RoleId">The role's PublicId (an ordinary role; a virtual role is refused).</param>
/// <param name="Scope">How far the role's permissions reach for the user.</param>
public sealed record RoleAssignmentInput([property: WireId] PublicId<RoleIds> RoleId, PermissionScope Scope);

/// <summary>One permission to grant directly.</summary>
/// <param name="Key">The permission key (in the live catalog).</param>
/// <param name="Scope">How far the grant reaches.</param>
public sealed record PermissionGrantInput(string Key, PermissionScope Scope);

/// <summary>
/// The body of <c>PUT /api/admin/access/users/{userId}/grants</c>: the user's complete set of roles and direct grants
/// (replaces both), validated by <see cref="PutUserGrantsValidator"/>. A role or key listed twice keeps its widest
/// scope.
/// </summary>
/// <param name="Roles">The ordinary roles the user holds after the write.</param>
/// <param name="Permissions">The direct grants the user holds after the write.</param>
/// <param name="ConfirmBreakGlassOnly">
/// True accepts that the write leaves only the break-glass administrator able to manage access (otherwise refused
/// with <see cref="AccessErrorCodes.LastManager"/>).
/// </param>
public sealed record PutUserGrantsCommand(
    IReadOnlyList<RoleAssignmentInput> Roles,
    IReadOnlyList<PermissionGrantInput> Permissions,
    bool ConfirmBreakGlassOnly = false);

/// <summary>Validates <see cref="PutUserGrantsCommand"/>: every field error at once, never throws.</summary>
public sealed class PutUserGrantsValidator : IValidator<PutUserGrantsCommand>
{
    /// <inheritdoc />
    public ValidationErrors Validate(PutUserGrantsCommand input)
    {
        ArgumentNullException.ThrowIfNull(input);
        var errors = new ValidationErrors();
        CheckRoles(errors, FieldNames.Of(nameof(PutUserGrantsCommand.Roles)), input.Roles);
        CheckPermissions(errors, FieldNames.Of(nameof(PutUserGrantsCommand.Permissions)), input.Permissions);
        return errors;
    }

    private static void CheckRoles(ValidationErrors errors, string field, IReadOnlyList<RoleAssignmentInput>? roles)
    {
        if (roles is null || roles.Any(role => role is null || !role.RoleId.HasValue))
        {
            errors.Add(field: field, message: ValidationMessages.Required);
            return;
        }

        if (roles.Count > AccessLimits.RolesPerUserMax)
        {
            errors.Add(field: field, message: ValidationMessages.TooLong);
        }

        if (roles.Any(role => !Enum.IsDefined(role.Scope)))
        {
            errors.Add(field: field, message: ValidationMessages.InvalidFormat);
        }
    }

    private static void CheckPermissions(ValidationErrors errors, string field, IReadOnlyList<PermissionGrantInput>? permissions)
    {
        if (permissions is null || permissions.Any(permission => permission is null))
        {
            errors.Add(field: field, message: ValidationMessages.Required);
            return;
        }

        if (permissions.Count > AccessLimits.PermissionsPerWriteMax)
        {
            errors.Add(field: field, message: ValidationMessages.TooLong);
        }

        if (permissions.Any(permission => !PermissionKeyFormat.IsValid(permission.Key) || !Enum.IsDefined(permission.Scope)))
        {
            errors.Add(field: field, message: ValidationMessages.InvalidFormat);
        }
    }
}

/// <summary>A validated grant write with its roles and catalog rows loaded (tracked) and duplicates merged.</summary>
/// <param name="Roles">Each role once, with its widest scope.</param>
/// <param name="Permissions">Each permission once, with its widest scope.</param>
public sealed record GrantRequest(
    IReadOnlyList<(Role Role, PermissionScope Scope)> Roles,
    IReadOnlyList<(PermissionEntry Permission, PermissionScope Scope)> Permissions);

/// <summary>
/// Turns a <see cref="PutUserGrantsCommand"/> into a <see cref="GrantRequest"/>: validation, unknown roles
/// (<see cref="AccessErrorCodes.UnknownRole"/>), virtual roles (<see cref="AccessErrorCodes.VirtualRoleNotAssignable"/>)
/// and keys outside the live catalog (<see cref="AccessErrorCodes.UnknownPermission"/>), all 400.
/// </summary>
public sealed class GrantRequestReader(
    IValidator<PutUserGrantsCommand> validator,
    IRoleRepository roles,
    GrantablePermissions grantable)
{
    /// <summary>The request, or the first refusal.</summary>
    public async Task<Outcome<GrantRequest>> ReadAsync(PutUserGrantsCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        var errors = validator.Validate(command);
        if (!errors.IsValid)
        {
            return Refusal.Invalid(errors);
        }

        var wantedRoles = Widest(command.Roles.Select(input => (input.RoleId.Value, input.Scope)));
        var found = await roles.FindManyAsync([.. wantedRoles.Keys], cancellationToken);
        if (found.Count != wantedRoles.Count)
        {
            return Refusal.Invalid(AccessErrorCodes.UnknownRole);
        }

        if (found.Any(role => role.IsVirtual))
        {
            return Refusal.Invalid(AccessErrorCodes.VirtualRoleNotAssignable);
        }

        var wantedKeys = Widest(command.Permissions.Select(input => (input.Key, input.Scope)));
        var rows = await grantable.LoadAsync(wantedKeys.Keys, cancellationToken);
        if (!rows.IsSuccess)
        {
            return rows.Refusal;
        }

        return new GrantRequest(
            Roles: [.. found.OrderBy(role => role.Id).Select(role => (role, wantedRoles[role.Id]))],
            Permissions: [.. rows.Value.Values.OrderBy(row => row.Key, StringComparer.Ordinal).Select(row => (row, wantedKeys[row.Key]))]);
    }

    private static Dictionary<TKey, PermissionScope> Widest<TKey>(IEnumerable<(TKey Key, PermissionScope Scope)> entries)
        where TKey : notnull
    {
        var widest = new Dictionary<TKey, PermissionScope>();
        foreach (var (key, scope) in entries)
        {
            widest[key] = widest.TryGetValue(key, out var held) ? PermissionResolution.Widest(held, scope) : scope;
        }

        return widest;
    }
}
