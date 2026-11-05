using Starter.Modules.Access.Domain;

namespace Starter.Modules.Access.Application;

/// <summary>What one synchronization changed.</summary>
/// <param name="Added">New catalog rows.</param>
/// <param name="Refreshed">Existing rows whose owner or description changed, or that were un-retired.</param>
/// <param name="Retired">Rows whose key left the code catalog.</param>
public sealed record PermissionSyncReport(int Added, int Refreshed, int Retired)
{
    /// <summary>True when the database already matched the catalog.</summary>
    public bool IsUnchanged => Added == 0 && Refreshed == 0 && Retired == 0;
}

/// <summary>
/// Writes the code catalog to the database, idempotently: inserts new keys, refreshes and un-retires existing ones,
/// and retires (never deletes) keys that left the catalog. A second run with the same catalog writes nothing.
/// </summary>
public sealed class PermissionSynchronizer(
    PermissionCatalog catalog,
    IPermissionRepository permissions,
    IAccessUnitOfWork unitOfWork)
{
    /// <summary>Synchronizes the database with the catalog and reports what changed.</summary>
    public async Task<PermissionSyncReport> SynchronizeAsync(CancellationToken cancellationToken)
    {
        var rows = (await permissions.ListAllAsync(cancellationToken)).ToDictionary(row => row.Key, StringComparer.Ordinal);
        var added = 0;
        var refreshed = 0;
        foreach (var definition in catalog.Definitions)
        {
            if (rows.TryGetValue(definition.Key, out var row))
            {
                refreshed += row.Refresh(owner: definition.Owner, description: definition.Description) ? 1 : 0;
                continue;
            }

            permissions.Add(PermissionEntry.Create(key: definition.Key, owner: definition.Owner, description: definition.Description));
            added++;
        }

        var retired = rows.Values.Where(row => !catalog.Contains(row.Key)).Count(row => row.Retire());
        var report = new PermissionSyncReport(Added: added, Refreshed: refreshed, Retired: retired);
        if (!report.IsUnchanged)
        {
            await unitOfWork.SaveChangesAsync(cancellationToken);
        }

        return report;
    }
}
