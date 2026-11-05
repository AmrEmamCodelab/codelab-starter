using Starter.Application.Authorization;
using Starter.Modules.Access.Domain;

namespace Starter.Modules.Access.Application;

/// <summary>
/// The kernel <see cref="IUserPermissionGrants"/>: adds the missing direct grants of a user (idempotent). Used by
/// account seeding; the grant editor arrives in phase 3. The resolver has no cache, so a grant applies on the next
/// request.
/// </summary>
public sealed class UserPermissionGrants(
    PermissionCatalog catalog,
    IPermissionRepository permissions,
    IUserPermissionGrantRepository grants,
    IAccessUnitOfWork unitOfWork,
    TimeProvider timeProvider) : IUserPermissionGrants
{
    private const string ListSeparator = ", ";

    /// <inheritdoc />
    public async Task EnsureGrantedAsync(long userId, IReadOnlyCollection<string> permissionKeys, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(permissionKeys);
        EnsureInCatalog(permissionKeys);
        var held = (await grants.GrantedKeysAsync(userId, cancellationToken)).ToHashSet(StringComparer.Ordinal);
        var missing = permissionKeys.Where(key => !held.Contains(key)).Distinct(StringComparer.Ordinal).ToList();
        if (missing.Count == 0)
        {
            return;
        }

        var rows = await permissions.FindByKeysAsync(missing, cancellationToken);
        EnsureSynchronized(keys: missing, rows: rows);
        var now = timeProvider.GetUtcNow();
        foreach (var row in rows)
        {
            grants.Add(UserPermissionGrant.Create(userId: userId, permission: row, grantedAt: now));
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);
    }

    private void EnsureInCatalog(IReadOnlyCollection<string> permissionKeys)
    {
        var unknown = permissionKeys.Where(key => !catalog.Contains(key)).ToList();
        if (unknown.Count > 0)
        {
            throw new InvalidOperationException($"Cannot grant permission keys that are not in the catalog: {string.Join(ListSeparator, unknown)}.");
        }
    }

    private static void EnsureSynchronized(List<string> keys, IReadOnlyList<PermissionEntry> rows)
    {
        if (rows.Count != keys.Count)
        {
            throw new InvalidOperationException(
                "The permission catalog is not in the database yet; the permission synchronizer must run before grants are made.");
        }
    }
}
