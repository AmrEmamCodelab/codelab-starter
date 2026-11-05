using Starter.Application.Results;
using Starter.Application.Validation;
using Starter.Modules.Access.Domain;

namespace Starter.Modules.Access.Application;

/// <summary>One role with its permission keys.</summary>
/// <param name="Id">The internal role id.</param>
/// <param name="Code">The stable code.</param>
/// <param name="NameEn">The English name.</param>
/// <param name="NameAr">The Arabic name.</param>
/// <param name="IsActive">False when the role grants nothing.</param>
/// <param name="VirtualKind">The users a virtual role reaches, or null for an assignable role.</param>
/// <param name="Permissions">The keys the role grants, sorted (retired keys included until the role is saved again).</param>
public sealed record RoleDetail(
    long Id,
    string Code,
    string NameEn,
    string NameAr,
    bool IsActive,
    VirtualRoleKind? VirtualKind,
    IReadOnlyList<string> Permissions)
{
    /// <summary>The detail of <paramref name="role"/>.</summary>
    public static RoleDetail Of(Role role)
    {
        ArgumentNullException.ThrowIfNull(role);
        return new RoleDetail(
            Id: role.Id,
            Code: role.Code,
            NameEn: role.NameEn,
            NameAr: role.NameAr,
            IsActive: role.IsActive,
            VirtualKind: role.VirtualKind,
            Permissions: [.. role.Permissions.Select(granted => granted.Permission.Key).Order(StringComparer.Ordinal)]);
    }
}

/// <summary>A role after a write, with the warnings the write raised.</summary>
/// <param name="Role">The role as saved.</param>
/// <param name="Warnings">Warning codes (<see cref="AccessErrorCodes.OnlyBreakGlassManages"/>); usually empty.</param>
public sealed record RoleWriteResult(RoleDetail Role, IReadOnlyList<string> Warnings);

/// <summary>Lists every role (virtual roles first). The permission is enforced by the endpoint.</summary>
public sealed class ListRoles(IRoleRepository roles)
{
    /// <summary>The role rows.</summary>
    public async Task<Outcome<IReadOnlyList<RoleSummary>>> ExecuteAsync(CancellationToken cancellationToken) =>
        Outcome.Success(await roles.ListAsync(cancellationToken));
}

/// <summary>Reads one role with its permissions. The permission is enforced by the endpoint.</summary>
public sealed class GetRole(IRoleRepository roles)
{
    /// <summary>The role, or a 404 refusal (<see cref="AccessErrorCodes.RoleNotFound"/>).</summary>
    public async Task<Outcome<RoleDetail>> ExecuteAsync(long roleId, CancellationToken cancellationToken) =>
        await roles.FindAsync(roleId, cancellationToken) is { } role ? RoleDetail.Of(role) : Refusal.NotFound(AccessErrorCodes.RoleNotFound);
}

/// <summary>Creates an ordinary, active role. The permission is enforced by the endpoint.</summary>
public sealed class CreateRole(
    IValidator<CreateRoleCommand> validator,
    IRoleRepository roles,
    GrantablePermissions grantable,
    AccessChanges changes)
{
    /// <summary>
    /// The new role; 400 for invalid input or an unknown/retired key, 409 for a code or name already in use.
    /// </summary>
    public async Task<Outcome<RoleWriteResult>> ExecuteAsync(CreateRoleCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        var errors = validator.Validate(command);
        if (!errors.IsValid)
        {
            return Refusal.Invalid(errors);
        }

        var names = new RoleNames(En: command.NameEn.Trim(), Ar: command.NameAr.Trim());
        if (await RefuseClashAsync(command.Code, names, cancellationToken) is { } clash)
        {
            return clash;
        }

        var keys = await grantable.LoadAsync(command.Permissions, cancellationToken);
        if (!keys.IsSuccess)
        {
            return keys.Refusal;
        }

        var role = Role.Create(code: command.Code, names: names, createdAt: changes.Now);
        role.ReplacePermissions([.. keys.Value.Values], changes.Now);
        roles.Add(role);
        await changes.CommitAsync(
            () => new AccessAuditEvent(AccessChangeKind.RoleCreated, role.Id, RoleCount: 0, PermissionCount: role.Permissions.Count, GuardVerdict.Allowed),
            cancellationToken);
        return new RoleWriteResult(RoleDetail.Of(role), Warnings: []);
    }

    private async Task<Refusal?> RefuseClashAsync(string code, RoleNames names, CancellationToken cancellationToken)
    {
        if (await roles.CodeExistsAsync(code, cancellationToken))
        {
            return Refusal.Conflict(AccessErrorCodes.RoleCodeTaken);
        }

        return await roles.NameTakenAsync(names, exceptRoleId: null, cancellationToken) ? Refusal.Conflict(AccessErrorCodes.RoleNameTaken) : null;
    }
}

/// <summary>
/// Changes a role's names, permissions and active state (virtual roles included: their permissions are editable).
/// Live: the role's holders see the change on their next request, with no session bump. The permission is enforced
/// by the endpoint.
/// </summary>
public sealed class UpdateRole(
    IValidator<UpdateRoleCommand> validator,
    IRoleRepository roles,
    GrantablePermissions grantable,
    ManagerContinuityGuard guard,
    AccessChanges changes)
{
    /// <summary>
    /// The saved role; 400 for invalid input or an unknown/retired key, 404 for an unknown role, 409 for a name in use
    /// or for removing the last manager (<see cref="AccessErrorCodes.LastManager"/>).
    /// </summary>
    public async Task<Outcome<RoleWriteResult>> ExecuteAsync(long roleId, UpdateRoleCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        var errors = validator.Validate(command);
        if (!errors.IsValid)
        {
            return Refusal.Invalid(errors);
        }

        if (await roles.FindAsync(roleId, cancellationToken) is not { } role)
        {
            return Refusal.NotFound(AccessErrorCodes.RoleNotFound);
        }

        var names = new RoleNames(En: command.NameEn.Trim(), Ar: command.NameAr.Trim());
        if (await roles.NameTakenAsync(names, exceptRoleId: roleId, cancellationToken))
        {
            return Refusal.Conflict(AccessErrorCodes.RoleNameTaken);
        }

        var keys = await grantable.LoadAsync(command.Permissions, cancellationToken);
        if (!keys.IsSuccess)
        {
            return keys.Refusal;
        }

        var change = new RoleChange(RoleId: roleId, IsActive: command.IsActive, Keys: [.. keys.Value.Keys]);
        var verdict = await guard.CheckAsync(change, command.ConfirmBreakGlassOnly, cancellationToken);
        return verdict is GuardVerdict.Refused
            ? Refusal.Conflict(AccessErrorCodes.LastManager)
            : await SaveAsync(role, new RoleEdit(names, command.IsActive, [.. keys.Value.Values]), verdict, cancellationToken);
    }

    private async Task<RoleWriteResult> SaveAsync(Role role, RoleEdit edit, GuardVerdict verdict, CancellationToken cancellationToken)
    {
        role.Rename(edit.Names, changes.Now);
        role.SetActive(edit.IsActive, changes.Now);
        role.ReplacePermissions(edit.Permissions, changes.Now);
        await changes.CommitAsync(
            () => new AccessAuditEvent(AccessChangeKind.RoleUpdated, role.Id, RoleCount: 0, PermissionCount: role.Permissions.Count, verdict),
            cancellationToken);
        return new RoleWriteResult(RoleDetail.Of(role), GuardWarnings.Of(verdict));
    }

    private sealed record RoleEdit(RoleNames Names, bool IsActive, IReadOnlyCollection<PermissionEntry> Permissions);
}

/// <summary>The warnings a guard verdict adds to a successful write (one owner).</summary>
public static class GuardWarnings
{
    private static readonly IReadOnlyList<string> None = [];
    private static readonly IReadOnlyList<string> BreakGlassOnly = [AccessErrorCodes.OnlyBreakGlassManages];

    /// <summary>The warning codes of <paramref name="verdict"/>.</summary>
    public static IReadOnlyList<string> Of(GuardVerdict verdict) => verdict is GuardVerdict.AllowedBreakGlassOnly ? BreakGlassOnly : None;
}
