using Starter.Application.Authorization;
using Starter.Application.Persistence;
using Starter.Modules.Access.Domain;

namespace Starter.Modules.Access.Application;

/// <summary>The permission catalog rows.</summary>
public interface IPermissionRepository
{
    /// <summary>Every row, current and retired (tracked, so changes are written by the unit of work).</summary>
    Task<IReadOnlyList<PermissionEntry>> ListAllAsync(CancellationToken cancellationToken);

    /// <summary>The rows with these keys (tracked); keys without a row are absent from the result.</summary>
    Task<IReadOnlyList<PermissionEntry>> FindByKeysAsync(IReadOnlyCollection<string> keys, CancellationToken cancellationToken);

    /// <summary>Stages a new row.</summary>
    void Add(PermissionEntry permission);
}

/// <summary>The direct user grants.</summary>
public interface IUserPermissionGrantRepository
{
    /// <summary>The keys of every permission granted to <paramref name="userId"/>, retired ones included.</summary>
    Task<IReadOnlyList<string>> GrantedKeysAsync(long userId, CancellationToken cancellationToken);

    /// <summary>The direct grants of <paramref name="userId"/> with their permission (tracked).</summary>
    Task<IReadOnlyList<UserPermissionGrant>> ForUserAsync(long userId, CancellationToken cancellationToken);

    /// <summary>Stages a new grant.</summary>
    void Add(UserPermissionGrant grant);

    /// <summary>Stages the removal of a grant.</summary>
    void Remove(UserPermissionGrant grant);
}

/// <summary>One row of the role list.</summary>
/// <param name="Id">The internal role id.</param>
/// <param name="Code">The stable code.</param>
/// <param name="NameEn">The English name.</param>
/// <param name="NameAr">The Arabic name.</param>
/// <param name="IsActive">False when the role grants nothing.</param>
/// <param name="VirtualKind">The users a virtual role reaches, or null for an assignable role.</param>
/// <param name="PermissionCount">How many permissions the role grants (retired ones included).</param>
public sealed record RoleSummary(
    long Id,
    string Code,
    string NameEn,
    string NameAr,
    bool IsActive,
    VirtualRoleKind? VirtualKind,
    int PermissionCount);

/// <summary>The roles.</summary>
public interface IRoleRepository
{
    /// <summary>Every role as a list row, virtual roles first, then by English name.</summary>
    Task<IReadOnlyList<RoleSummary>> ListAsync(CancellationToken cancellationToken);

    /// <summary>The role with its permissions (tracked), or null.</summary>
    Task<Role?> FindAsync(long roleId, CancellationToken cancellationToken);

    /// <summary>The roles with these ids (tracked); ids without a role are absent from the result.</summary>
    Task<IReadOnlyList<Role>> FindManyAsync(IReadOnlyCollection<long> roleIds, CancellationToken cancellationToken);

    /// <summary>True when a role already uses <paramref name="code"/> (case-insensitive).</summary>
    Task<bool> CodeExistsAsync(string code, CancellationToken cancellationToken);

    /// <summary>
    /// True when a role other than <paramref name="exceptRoleId"/> already uses either name (case-insensitive).
    /// </summary>
    Task<bool> NameTakenAsync(RoleNames names, long? exceptRoleId, CancellationToken cancellationToken);

    /// <summary>Stages a new role.</summary>
    void Add(Role role);
}

/// <summary>The role assignments of users.</summary>
public interface IUserRoleAssignmentRepository
{
    /// <summary>The assignments of <paramref name="userId"/> with their role (tracked).</summary>
    Task<IReadOnlyList<UserRoleAssignment>> ForUserAsync(long userId, CancellationToken cancellationToken);

    /// <summary>Stages a new assignment.</summary>
    void Add(UserRoleAssignment assignment);

    /// <summary>Stages the removal of an assignment.</summary>
    void Remove(UserRoleAssignment assignment);
}

/// <summary>A role as the resolver sees it.</summary>
/// <param name="RoleId">The internal role id.</param>
/// <param name="Code">The stable code.</param>
/// <param name="NameEn">The English name.</param>
/// <param name="NameAr">The Arabic name.</param>
/// <param name="IsActive">False when the role grants nothing.</param>
/// <param name="VirtualKind">The users a virtual role reaches, or null.</param>
/// <param name="Keys">The keys of the role's permissions, retired ones included.</param>
public sealed record RoleGrants(
    long RoleId,
    string Code,
    string NameEn,
    string NameAr,
    bool IsActive,
    VirtualRoleKind? VirtualKind,
    IReadOnlyList<string> Keys);

/// <summary>An ordinary role assigned to the user, with its scope.</summary>
/// <param name="Role">The role.</param>
/// <param name="Scope">How far it reaches for this user.</param>
public sealed record AssignedRole(RoleGrants Role, PermissionScope Scope);

/// <summary>A direct grant.</summary>
/// <param name="Key">The permission key.</param>
/// <param name="Scope">How far it reaches.</param>
public sealed record DirectGrant(string Key, PermissionScope Scope);

/// <summary>Everything this module stores that can grant a user a permission.</summary>
/// <param name="Direct">The user's direct grants.</param>
/// <param name="Assigned">The ordinary roles assigned to the user.</param>
/// <param name="VirtualRoles">Every virtual role (the resolver keeps the ones that match the user's kind).</param>
public sealed record UserGrantSnapshot(
    IReadOnlyList<DirectGrant> Direct,
    IReadOnlyList<AssignedRole> Assigned,
    IReadOnlyList<RoleGrants> VirtualRoles);

/// <summary>Reads a user's grant snapshot (untracked, three indexed queries, no cache).</summary>
public interface IUserGrantReader
{
    /// <summary>The grants of <paramref name="userId"/>.</summary>
    Task<UserGrantSnapshot> ReadAsync(long userId, CancellationToken cancellationToken);
}

/// <summary>Reads the part of the grant graph that can make someone a manager (see <see cref="ManagerGraph"/>).</summary>
public interface IManagerGraphReader
{
    /// <summary>
    /// Every role with the subset of <paramref name="keys"/> it grants, and every user holding at least one of
    /// <paramref name="keys"/> through a direct grant or an assigned role.
    /// </summary>
    Task<ManagerGraph> ReadAsync(IReadOnlySet<string> keys, CancellationToken cancellationToken);
}

/// <summary>The Access module's unit of work (implemented by its persistence context in Infrastructure).</summary>
public interface IAccessUnitOfWork : IUnitOfWork;
