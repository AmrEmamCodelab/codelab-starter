using Codelab.Security.PublicIds;
using Starter.Application.Authorization;
using Starter.Application.Users;
using Starter.Domain.Users;
using Starter.Modules.Access.Application;
using Starter.Modules.Access.Domain;

namespace Starter.Modules.Access.Api;

/// <summary>One catalog permission (<c>GET /api/admin/access/permissions</c>).</summary>
/// <param name="Key">The key; the editors build their tree from its dotted segments.</param>
/// <param name="Module">The owner segment.</param>
/// <param name="Description">The English description.</param>
/// <param name="IsRetired">True when the key left the code (grants nothing, cannot be granted).</param>
public sealed record PermissionResponse(string Key, string Module, string Description, bool IsRetired)
{
    /// <summary>The response of <paramref name="info"/>.</summary>
    public static PermissionResponse From(PermissionInfo info)
    {
        ArgumentNullException.ThrowIfNull(info);
        return new PermissionResponse(Key: info.Key, Module: info.Module, Description: info.Description, IsRetired: info.IsRetired);
    }
}

/// <summary>One row of <c>GET /api/admin/access/roles</c>.</summary>
/// <param name="Id">The role's PublicId.</param>
/// <param name="Code">The stable code.</param>
/// <param name="NameEn">The English name.</param>
/// <param name="NameAr">The Arabic name.</param>
/// <param name="IsActive">False when the role grants nothing.</param>
/// <param name="IsVirtual">True for a virtual role (never assignable).</param>
/// <param name="VirtualKind">The users a virtual role reaches (int), or null.</param>
/// <param name="PermissionCount">How many permissions the role grants.</param>
public sealed record RoleSummaryResponse(
    [property: WireId] PublicId<RoleIds> Id,
    string Code,
    string NameEn,
    string NameAr,
    bool IsActive,
    bool IsVirtual,
    VirtualRoleKind? VirtualKind,
    int PermissionCount)
{
    /// <summary>The response of <paramref name="role"/>.</summary>
    public static RoleSummaryResponse From(RoleSummary role)
    {
        ArgumentNullException.ThrowIfNull(role);
        return new RoleSummaryResponse(
            Id: PublicId<RoleIds>.From(role.Id),
            Code: role.Code,
            NameEn: role.NameEn,
            NameAr: role.NameAr,
            IsActive: role.IsActive,
            IsVirtual: role.VirtualKind is not null,
            VirtualKind: role.VirtualKind,
            PermissionCount: role.PermissionCount);
    }
}

/// <summary>One role with its permissions (GET, POST and PUT of the roles routes).</summary>
/// <param name="Id">The role's PublicId.</param>
/// <param name="Code">The stable code.</param>
/// <param name="NameEn">The English name.</param>
/// <param name="NameAr">The Arabic name.</param>
/// <param name="IsActive">False when the role grants nothing.</param>
/// <param name="IsVirtual">True for a virtual role.</param>
/// <param name="VirtualKind">The users a virtual role reaches (int), or null.</param>
/// <param name="Permissions">The keys the role grants, sorted.</param>
/// <param name="Warnings">Warning codes of the write (<c>ACCESS_ONLY_BREAK_GLASS_MANAGES</c>); empty on a read.</param>
public sealed record RoleResponse(
    [property: WireId] PublicId<RoleIds> Id,
    string Code,
    string NameEn,
    string NameAr,
    bool IsActive,
    bool IsVirtual,
    VirtualRoleKind? VirtualKind,
    IReadOnlyList<string> Permissions,
    IReadOnlyList<string> Warnings)
{
    /// <summary>The response of a read.</summary>
    public static RoleResponse From(RoleDetail role) => From(new RoleWriteResult(role, Warnings: []));

    /// <summary>The response of a write.</summary>
    public static RoleResponse From(RoleWriteResult result)
    {
        ArgumentNullException.ThrowIfNull(result);
        var role = result.Role;
        return new RoleResponse(
            Id: PublicId<RoleIds>.From(role.Id),
            Code: role.Code,
            NameEn: role.NameEn,
            NameAr: role.NameAr,
            IsActive: role.IsActive,
            IsVirtual: role.VirtualKind is not null,
            VirtualKind: role.VirtualKind,
            Permissions: role.Permissions,
            Warnings: result.Warnings);
    }
}

/// <summary>One account of the user picker (<c>GET /api/admin/access/users</c>).</summary>
/// <param name="Id">The user's PublicId (label <c>users</c>).</param>
/// <param name="UserName">The sign-in name.</param>
/// <param name="DisplayName">The display name.</param>
/// <param name="IsActive">False for a deactivated account.</param>
public sealed record AccessUserResponse([property: WireId] PublicId<UserIds> Id, string UserName, string DisplayName, bool IsActive)
{
    /// <summary>The response of <paramref name="account"/>.</summary>
    public static AccessUserResponse From(AccountSummary account)
    {
        ArgumentNullException.ThrowIfNull(account);
        return new AccessUserResponse(
            Id: PublicId<UserIds>.From(account.UserId),
            UserName: account.UserName,
            DisplayName: account.DisplayName,
            IsActive: account.IsActive);
    }
}

/// <summary>The user of a grants response.</summary>
/// <param name="Id">The user's PublicId.</param>
/// <param name="UserName">The sign-in name.</param>
/// <param name="DisplayName">The display name.</param>
/// <param name="IsActive">False for a deactivated account (it holds nothing).</param>
/// <param name="Kind">Internal (1) or external (2): selects the virtual roles.</param>
/// <param name="IsBreakGlass">True for the break-glass administrator (holds everything; not editable).</param>
public sealed record GrantUserResponse(
    [property: WireId] PublicId<UserIds> Id,
    string UserName,
    string DisplayName,
    bool IsActive,
    UserKind Kind,
    bool IsBreakGlass);

/// <summary>An assigned role of a grants response.</summary>
/// <param name="RoleId">The role's PublicId.</param>
/// <param name="Code">The stable code.</param>
/// <param name="NameEn">The English name.</param>
/// <param name="NameAr">The Arabic name.</param>
/// <param name="IsActive">False when the role grants nothing.</param>
/// <param name="Scope">Global (1) or own organization (2).</param>
public sealed record AssignedRoleResponse(
    [property: WireId] PublicId<RoleIds> RoleId,
    string Code,
    string NameEn,
    string NameAr,
    bool IsActive,
    PermissionScope Scope);

/// <summary>A direct grant of a grants response.</summary>
/// <param name="Key">The permission key.</param>
/// <param name="Scope">Global (1) or own organization (2).</param>
public sealed record DirectGrantResponse(string Key, PermissionScope Scope);

/// <summary>A virtual role that reaches the user.</summary>
/// <param name="RoleId">The role's PublicId.</param>
/// <param name="Code">The stable code.</param>
/// <param name="NameEn">The English name.</param>
/// <param name="NameAr">The Arabic name.</param>
/// <param name="IsActive">False when the role grants nothing.</param>
/// <param name="VirtualKind">Which users it reaches (int).</param>
public sealed record VirtualRoleResponse(
    [property: WireId] PublicId<RoleIds> RoleId,
    string Code,
    string NameEn,
    string NameAr,
    bool IsActive,
    VirtualRoleKind VirtualKind);

/// <summary>One source of an effective permission.</summary>
/// <param name="Kind">Direct (1), role (2), virtual role (3) or break-glass (4).</param>
/// <param name="RoleId">The role's PublicId, or null for a direct grant and break-glass.</param>
/// <param name="Scope">The scope this source grants.</param>
public sealed record PermissionSourceResponse(PermissionSourceKind Kind, [property: WireId] PublicId<RoleIds>? RoleId, PermissionScope Scope);

/// <summary>One effective permission.</summary>
/// <param name="Key">The key.</param>
/// <param name="Scope">The widest scope (global wins).</param>
/// <param name="Sources">Every source.</param>
public sealed record EffectivePermissionResponse(string Key, PermissionScope Scope, IReadOnlyList<PermissionSourceResponse> Sources);
