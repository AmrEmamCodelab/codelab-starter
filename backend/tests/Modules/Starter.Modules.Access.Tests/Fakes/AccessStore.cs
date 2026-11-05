using System.Reflection;
using Starter.Application.Authorization;
using Starter.Modules.Access.Application;
using Starter.Modules.Access.Domain;

namespace Starter.Modules.Access.Tests.Fakes;

/// <summary>Sets the private database id of an entity, as EF would on save.</summary>
internal static class EntityIds
{
    public static T WithId<T>(this T entity, long id)
        where T : class
    {
        typeof(T).GetProperty("Id", BindingFlags.Public | BindingFlags.Instance)!.SetValue(entity, id);
        return entity;
    }

    public static Role AsVirtual(this Role role, VirtualRoleKind kind)
    {
        typeof(Role).GetProperty(nameof(Role.VirtualKind))!.SetValue(role, kind);
        return role;
    }
}

/// <summary>
/// An in-memory Access database behind every repository and reader port, so the use cases run against one shared state.
/// <see cref="SaveChangesAsync"/> gives new roles their ids, as the database would.
/// </summary>
internal sealed class AccessStore :
    IPermissionRepository,
    IRoleRepository,
    IUserRoleAssignmentRepository,
    IUserGrantReader,
    IManagerGraphReader,
    IAccessUnitOfWork
{
    public static readonly DateTimeOffset Epoch = DateTimeOffset.UnixEpoch;

    private long _nextRoleId = 100;

    public List<PermissionEntry> Permissions { get; } = [];

    public List<Role> Roles { get; } = [];

    public List<UserRoleAssignment> Assignments { get; } = [];

    public FakeGrantRepository Grants { get; } = new();

    public int Saves { get; private set; }

    /// <summary>Adds catalog rows for <paramref name="keys"/> (ids from 1).</summary>
    public AccessStore WithPermissions(params string[] keys)
    {
        foreach (var key in keys)
        {
            Permissions.Add(PermissionEntry.Create(key: key, owner: key.Split('.')[0], description: "Test " + key).WithId(Permissions.Count + 1));
        }

        return this;
    }

    public PermissionEntry Permission(string key) => Permissions.Single(row => row.Key == key);

    /// <summary>Adds a saved ordinary role granting <paramref name="keys"/>.</summary>
    public Role AddRole(string code, params string[] keys)
    {
        var role = Role.Create(code: code, names: new RoleNames(En: code + " en", Ar: code + " ar"), createdAt: Epoch).WithId(_nextRoleId++);
        role.ReplacePermissions([.. keys.Select(Permission)], Epoch);
        Roles.Add(role);
        return role;
    }

    /// <summary>Adds a saved virtual role granting <paramref name="keys"/>.</summary>
    public Role AddVirtualRole(VirtualRoleKind kind, params string[] keys) => AddRole("virtual-" + (int)kind, keys).AsVirtual(kind);

    public void Assign(long userId, Role role, PermissionScope scope = PermissionScope.Global) =>
        Assignments.Add(UserRoleAssignment.Create(userId: userId, role: role, scope: scope, assignedAt: Epoch));

    public void GrantDirect(long userId, string key, PermissionScope scope = PermissionScope.Global) =>
        Grants.Add(UserPermissionGrant.Create(userId: userId, permission: Permission(key), grantedAt: Epoch, scope: scope));

    Task<IReadOnlyList<PermissionEntry>> IPermissionRepository.ListAllAsync(CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<PermissionEntry>>([.. Permissions]);

    Task<IReadOnlyList<PermissionEntry>> IPermissionRepository.FindByKeysAsync(IReadOnlyCollection<string> keys, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<PermissionEntry>>([.. Permissions.Where(row => keys.Contains(row.Key))]);

    void IPermissionRepository.Add(PermissionEntry permission) => Permissions.Add(permission);

    Task<IReadOnlyList<RoleSummary>> IRoleRepository.ListAsync(CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<RoleSummary>>(
        [
            .. Roles.OrderBy(role => role.VirtualKind is null).ThenBy(role => role.VirtualKind).ThenBy(role => role.NameEn, StringComparer.Ordinal)
                .Select(role => new RoleSummary(role.Id, role.Code, role.NameEn, role.NameAr, role.IsActive, role.VirtualKind, role.Permissions.Count)),
        ]);

    Task<Role?> IRoleRepository.FindAsync(long roleId, CancellationToken cancellationToken) =>
        Task.FromResult(Roles.FirstOrDefault(role => role.Id == roleId));

    Task<IReadOnlyList<Role>> IRoleRepository.FindManyAsync(IReadOnlyCollection<long> roleIds, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<Role>>([.. Roles.Where(role => roleIds.Contains(role.Id))]);

    Task<bool> IRoleRepository.CodeExistsAsync(string code, CancellationToken cancellationToken) =>
        Task.FromResult(Roles.Any(role => string.Equals(role.Code, code, StringComparison.OrdinalIgnoreCase)));

    Task<bool> IRoleRepository.NameTakenAsync(RoleNames names, long? exceptRoleId, CancellationToken cancellationToken) =>
        Task.FromResult(Roles.Any(role => role.Id != exceptRoleId
            && (string.Equals(role.NameEn, names.En, StringComparison.OrdinalIgnoreCase) || string.Equals(role.NameAr, names.Ar, StringComparison.OrdinalIgnoreCase))));

    void IRoleRepository.Add(Role role) => Roles.Add(role);

    Task<IReadOnlyList<UserRoleAssignment>> IUserRoleAssignmentRepository.ForUserAsync(long userId, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<UserRoleAssignment>>([.. Assignments.Where(assignment => assignment.UserId == userId)]);

    void IUserRoleAssignmentRepository.Add(UserRoleAssignment assignment) => Assignments.Add(assignment);

    void IUserRoleAssignmentRepository.Remove(UserRoleAssignment assignment) => Assignments.Remove(assignment);

    Task<UserGrantSnapshot> IUserGrantReader.ReadAsync(long userId, CancellationToken cancellationToken) =>
        Task.FromResult(new UserGrantSnapshot(
            Direct: [.. Grants.Grants.Where(grant => grant.UserId == userId).Select(grant => new DirectGrant(grant.Permission.Key, grant.Scope))],
            Assigned: [.. Assignments.Where(assignment => assignment.UserId == userId).Select(assignment => new AssignedRole(GrantsOf(assignment.Role), assignment.Scope))],
            VirtualRoles: [.. Roles.Where(role => role.IsVirtual).Select(GrantsOf)]));

    Task<ManagerGraph> IManagerGraphReader.ReadAsync(IReadOnlySet<string> keys, CancellationToken cancellationToken)
    {
        var roles = Roles.Select(role => new ManagerRole(
            role.Id,
            role.IsActive,
            role.VirtualKind,
            role.Permissions.Select(granted => granted.Permission).Where(row => !row.IsObsolete && keys.Contains(row.Key)).Select(row => row.Key).ToHashSet(StringComparer.Ordinal))).ToList();
        var keyRoles = roles.Where(role => role.Keys.Count > 0).Select(role => role.RoleId).ToHashSet();
        var direct = Grants.Grants.Where(grant => !grant.Permission.IsObsolete && keys.Contains(grant.Permission.Key)).ToList();
        var assigned = Assignments.Where(assignment => keyRoles.Contains(assignment.RoleId)).ToList();
        var candidates = direct.Select(grant => grant.UserId).Concat(assigned.Select(assignment => assignment.UserId)).Distinct()
            .Select(userId => new ManagerCandidate(
                userId,
                assigned.Where(assignment => assignment.UserId == userId).Select(assignment => assignment.RoleId).ToHashSet(),
                direct.Where(grant => grant.UserId == userId).Select(grant => grant.Permission.Key).ToHashSet(StringComparer.Ordinal)));
        return Task.FromResult(new ManagerGraph(roles, candidates));
    }

    public Task<int> SaveChangesAsync(CancellationToken cancellationToken)
    {
        foreach (var role in Roles.Where(role => role.Id == 0))
        {
            role.WithId(_nextRoleId++);
        }

        Saves++;
        return Task.FromResult(Saves);
    }

    private static RoleGrants GrantsOf(Role role) => new(
        role.Id,
        role.Code,
        role.NameEn,
        role.NameAr,
        role.IsActive,
        role.VirtualKind,
        [.. role.Permissions.Select(granted => granted.Permission.Key)]);
}
