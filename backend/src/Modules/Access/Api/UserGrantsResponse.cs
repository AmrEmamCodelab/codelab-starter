using Codelab.Security.PublicIds;
using Starter.Modules.Access.Application;

namespace Starter.Modules.Access.Api;

/// <summary>
/// The body of <c>GET</c> and <c>PUT /api/admin/access/users/{userId}/grants</c>: the editable rows (roles and direct
/// permissions), the virtual roles that reach the user, and what the user holds now with every source.
/// </summary>
/// <param name="User">The user.</param>
/// <param name="Roles">The assigned ordinary roles with their scopes.</param>
/// <param name="Permissions">The direct grants with their scopes.</param>
/// <param name="VirtualRoles">The virtual roles that match the user's kind.</param>
/// <param name="Effective">The live effective permissions.</param>
/// <param name="Warnings">Warning codes of the write; empty on a read.</param>
public sealed record UserGrantsResponse(
    GrantUserResponse User,
    IReadOnlyList<AssignedRoleResponse> Roles,
    IReadOnlyList<DirectGrantResponse> Permissions,
    IReadOnlyList<VirtualRoleResponse> VirtualRoles,
    IReadOnlyList<EffectivePermissionResponse> Effective,
    IReadOnlyList<string> Warnings)
{
    /// <summary>The response of <paramref name="view"/>.</summary>
    public static UserGrantsResponse From(UserGrantsView view)
    {
        ArgumentNullException.ThrowIfNull(view);
        var account = view.Account;
        return new UserGrantsResponse(
            User: new GrantUserResponse(
                Id: PublicId<UserIds>.From(account.UserId),
                UserName: account.UserName,
                DisplayName: account.DisplayName,
                IsActive: account.IsActive,
                Kind: account.Kind,
                IsBreakGlass: account.IsBreakGlass),
            Roles: [.. view.Roles.Select(AssignedOf)],
            Permissions: [.. view.Permissions.Select(grant => new DirectGrantResponse(Key: grant.Key, Scope: grant.Scope))],
            VirtualRoles: [.. view.VirtualRoles.Select(VirtualOf)],
            Effective: [.. view.Effective.Select(EffectiveOf)],
            Warnings: view.Warnings);
    }

    private static AssignedRoleResponse AssignedOf(AssignedRole assigned) => new(
        RoleId: PublicId<RoleIds>.From(assigned.Role.RoleId),
        Code: assigned.Role.Code,
        NameEn: assigned.Role.NameEn,
        NameAr: assigned.Role.NameAr,
        IsActive: assigned.Role.IsActive,
        Scope: assigned.Scope);

    private static VirtualRoleResponse VirtualOf(RoleGrants role) => new(
        RoleId: PublicId<RoleIds>.From(role.RoleId),
        Code: role.Code,
        NameEn: role.NameEn,
        NameAr: role.NameAr,
        IsActive: role.IsActive,
        VirtualKind: role.VirtualKind ?? throw new InvalidOperationException($"Role {role.RoleId} is listed as virtual without a kind."));

    private static EffectivePermissionResponse EffectiveOf(EffectiveGrant permission) => new(
        Key: permission.Key,
        Scope: permission.Scope,
        Sources:
        [
            .. permission.Sources.Select(source => new PermissionSourceResponse(
                Kind: source.Kind,
                RoleId: source.RoleId is { } roleId ? PublicId<RoleIds>.From(roleId) : null,
                Scope: source.Scope)),
        ]);
}
