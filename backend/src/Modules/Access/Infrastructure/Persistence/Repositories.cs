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
    public void Add(UserPermissionGrant grant) => context.UserPermissionGrants.Add(grant);
}
