using Microsoft.EntityFrameworkCore;
using Starter.Modules.Access.Application;
using Starter.Modules.Access.Domain;

namespace Starter.Modules.Access.Infrastructure.Persistence;

/// <summary>EF Core <see cref="IPermissionRepository"/>.</summary>
public sealed class PermissionRepository(AccessDbContext context) : IPermissionRepository
{
    /// <inheritdoc />
    public async Task<IReadOnlyList<PermissionEntry>> ListAllAsync(CancellationToken cancellationToken) =>
        await context.Permissions.ToListAsync(cancellationToken);

    /// <inheritdoc />
    public async Task<IReadOnlyList<PermissionEntry>> FindByKeysAsync(IReadOnlyCollection<string> keys, CancellationToken cancellationToken) =>
        await context.Permissions.Where(permission => keys.Contains(permission.Key)).ToListAsync(cancellationToken);

    /// <inheritdoc />
    public void Add(PermissionEntry permission) => context.Permissions.Add(permission);
}

/// <summary>EF Core <see cref="IUserPermissionGrantRepository"/>.</summary>
public sealed class UserPermissionGrantRepository(AccessDbContext context) : IUserPermissionGrantRepository
{
    /// <inheritdoc />
    public async Task<IReadOnlyList<string>> GrantedKeysAsync(long userId, CancellationToken cancellationToken) =>
        await context.UserPermissionGrants
            .AsNoTracking()
            .Where(grant => grant.UserId == userId)
            .Select(grant => grant.Permission.Key)
            .ToListAsync(cancellationToken);

    /// <inheritdoc />
    public async Task<IReadOnlyList<UserPermissionGrant>> ForUserAsync(long userId, CancellationToken cancellationToken) =>
        await context.UserPermissionGrants
            .Include(grant => grant.Permission)
            .Where(grant => grant.UserId == userId)
            .ToListAsync(cancellationToken);

    /// <inheritdoc />
    public void Add(UserPermissionGrant grant) => context.UserPermissionGrants.Add(grant);

    /// <inheritdoc />
    public void Remove(UserPermissionGrant grant) => context.UserPermissionGrants.Remove(grant);
}

/// <summary>EF Core <see cref="IRoleRepository"/>. Name and code comparisons use the database collation (case-insensitive).</summary>
public sealed class RoleRepository(AccessDbContext context) : IRoleRepository
{
    /// <inheritdoc />
    public async Task<IReadOnlyList<RoleSummary>> ListAsync(CancellationToken cancellationToken)
    {
        var rows = await context.Roles
            .AsNoTracking()
            .OrderBy(role => role.VirtualKind == null)
            .ThenBy(role => role.VirtualKind)
            .ThenBy(role => role.NameEn)
            .Select(role => new { role.Id, role.Code, role.NameEn, role.NameAr, role.IsActive, role.VirtualKind, PermissionCount = role.Permissions.Count })
            .ToListAsync(cancellationToken);
        return
        [
            .. rows.Select(row => new RoleSummary(
                Id: row.Id,
                Code: row.Code,
                NameEn: row.NameEn,
                NameAr: row.NameAr,
                IsActive: row.IsActive,
                VirtualKind: row.VirtualKind,
                PermissionCount: row.PermissionCount)),
        ];
    }

    /// <inheritdoc />
    public async Task<Role?> FindAsync(long roleId, CancellationToken cancellationToken) =>
        await context.Roles
            .Include(role => role.Permissions)
            .ThenInclude(granted => granted.Permission)
            .FirstOrDefaultAsync(role => role.Id == roleId, cancellationToken);

    /// <inheritdoc />
    public async Task<IReadOnlyList<Role>> FindManyAsync(IReadOnlyCollection<long> roleIds, CancellationToken cancellationToken) =>
        roleIds.Count == 0 ? [] : await context.Roles.Where(role => roleIds.Contains(role.Id)).ToListAsync(cancellationToken);

    /// <inheritdoc />
    public async Task<bool> CodeExistsAsync(string code, CancellationToken cancellationToken) =>
        await context.Roles.AnyAsync(role => role.Code == code, cancellationToken);

    /// <inheritdoc />
    public async Task<bool> NameTakenAsync(RoleNames names, long? exceptRoleId, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(names);
        return await context.Roles.AnyAsync(
            role => (exceptRoleId == null || role.Id != exceptRoleId) && (role.NameEn == names.En || role.NameAr == names.Ar),
            cancellationToken);
    }

    /// <inheritdoc />
    public void Add(Role role) => context.Roles.Add(role);
}

/// <summary>EF Core <see cref="IUserRoleAssignmentRepository"/>.</summary>
public sealed class UserRoleAssignmentRepository(AccessDbContext context) : IUserRoleAssignmentRepository
{
    /// <inheritdoc />
    public async Task<IReadOnlyList<UserRoleAssignment>> ForUserAsync(long userId, CancellationToken cancellationToken) =>
        await context.UserRoleAssignments
            .Include(assignment => assignment.Role)
            .Where(assignment => assignment.UserId == userId)
            .ToListAsync(cancellationToken);

    /// <inheritdoc />
    public void Add(UserRoleAssignment assignment) => context.UserRoleAssignments.Add(assignment);

    /// <inheritdoc />
    public void Remove(UserRoleAssignment assignment) => context.UserRoleAssignments.Remove(assignment);
}
