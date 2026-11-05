using Microsoft.EntityFrameworkCore;
using Starter.Modules.Access.Application;
using Starter.Modules.Access.Domain;

namespace Starter.Modules.Access.Infrastructure.Persistence;

/// <summary>
/// EF Core <see cref="IUserGrantReader"/>: three untracked, indexed queries per resolve (direct grants, assigned roles
/// with their keys, virtual roles with their keys). No cache: a change is visible on the next request.
/// </summary>
public sealed class UserGrantReader(AccessDbContext context) : IUserGrantReader
{
    /// <inheritdoc />
    public async Task<UserGrantSnapshot> ReadAsync(long userId, CancellationToken cancellationToken)
    {
        var direct = await context.UserPermissionGrants
            .AsNoTracking()
            .Where(grant => grant.UserId == userId)
            .Select(grant => new { grant.Permission.Key, grant.Scope })
            .ToListAsync(cancellationToken);
        var assigned = await context.UserRoleAssignments
            .AsNoTracking()
            .Where(assignment => assignment.UserId == userId)
            .Select(assignment => new { assignment.Scope, Role = assignment.Role, Keys = assignment.Role.Permissions.Select(granted => granted.Permission.Key).ToList() })
            .ToListAsync(cancellationToken);
        var virtualRoles = await context.Roles
            .AsNoTracking()
            .Where(role => role.VirtualKind != null)
            .Select(role => new { Role = role, Keys = role.Permissions.Select(granted => granted.Permission.Key).ToList() })
            .ToListAsync(cancellationToken);
        return new UserGrantSnapshot(
            Direct: [.. direct.Select(grant => new DirectGrant(Key: grant.Key, Scope: grant.Scope))],
            Assigned: [.. assigned.Select(row => new AssignedRole(Grants(row.Role, row.Keys), row.Scope))],
            VirtualRoles: [.. virtualRoles.Select(row => Grants(row.Role, row.Keys))]);
    }

    private static RoleGrants Grants(Role role, List<string> keys) => new(
        RoleId: role.Id,
        Code: role.Code,
        NameEn: role.NameEn,
        NameAr: role.NameAr,
        IsActive: role.IsActive,
        VirtualKind: role.VirtualKind,
        Keys: keys);
}

/// <summary>
/// EF Core <see cref="IManagerGraphReader"/>: every role with its subset of the given keys (roles are few), and every
/// user who holds one of the keys directly or through an assignment, with their assigned role ids.
/// </summary>
public sealed class ManagerGraphReader(AccessDbContext context) : IManagerGraphReader
{
    /// <inheritdoc />
    public async Task<ManagerGraph> ReadAsync(IReadOnlySet<string> keys, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(keys);
        var wanted = keys.ToList();
        var roles = await ReadRolesAsync(wanted, cancellationToken);
        var keyRoleIds = roles.Where(role => role.Keys.Count > 0).Select(role => role.RoleId).ToList();
        return new ManagerGraph(roles, await ReadCandidatesAsync(wanted, keyRoleIds, cancellationToken));
    }

    private async Task<List<ManagerRole>> ReadRolesAsync(List<string> wanted, CancellationToken cancellationToken)
    {
        var roles = await context.Roles
            .AsNoTracking()
            .Select(role => new
            {
                role.Id,
                role.IsActive,
                role.VirtualKind,
                Keys = role.Permissions.Where(granted => !granted.Permission.IsObsolete && wanted.Contains(granted.Permission.Key)).Select(granted => granted.Permission.Key).ToList(),
            })
            .ToListAsync(cancellationToken);
        return
        [
            .. roles.Select(role => new ManagerRole(
                RoleId: role.Id,
                IsActive: role.IsActive,
                VirtualKind: role.VirtualKind,
                Keys: role.Keys.ToHashSet(StringComparer.Ordinal))),
        ];
    }

    private async Task<List<ManagerCandidate>> ReadCandidatesAsync(List<string> wanted, List<long> keyRoleIds, CancellationToken cancellationToken)
    {
        var direct = await context.UserPermissionGrants
            .AsNoTracking()
            .Where(grant => !grant.Permission.IsObsolete && wanted.Contains(grant.Permission.Key))
            .Select(grant => new { grant.UserId, grant.Permission.Key })
            .ToListAsync(cancellationToken);
        var assignments = await context.UserRoleAssignments
            .AsNoTracking()
            .Where(assignment => keyRoleIds.Contains(assignment.RoleId))
            .Select(assignment => new { assignment.UserId, assignment.RoleId })
            .ToListAsync(cancellationToken);
        return
        [
            .. direct.Select(grant => grant.UserId).Concat(assignments.Select(assignment => assignment.UserId)).Distinct()
                .Select(userId => new ManagerCandidate(
                    UserId: userId,
                    RoleIds: assignments.Where(assignment => assignment.UserId == userId).Select(assignment => assignment.RoleId).ToHashSet(),
                    DirectKeys: direct.Where(grant => grant.UserId == userId).Select(grant => grant.Key).ToHashSet(StringComparer.Ordinal))),
        ];
    }
}
