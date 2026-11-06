using Microsoft.EntityFrameworkCore;
using Starter.Modules.Settings.Application;
using Starter.Modules.Settings.Domain;

namespace Starter.Modules.Settings.Infrastructure.Persistence;

/// <summary>The settings for the admin use cases (tracked).</summary>
public sealed class SettingsRepository(SettingsDbContext context) : ISettingsRepository
{
    /// <inheritdoc />
    public async Task<IReadOnlyList<Setting>> ListAsync(CancellationToken cancellationToken) =>
        await context.Settings
            .OrderBy(setting => setting.Group)
            .ThenBy(setting => setting.Key)
            .ToListAsync(cancellationToken);

    /// <inheritdoc />
    public async Task<Setting?> FindAsync(string key, CancellationToken cancellationToken)
    {
        // The key column uses the database collation (case-insensitive by default); the port promises ordinal.
        var setting = await context.Settings.FirstOrDefaultAsync(candidate => candidate.Key == key, cancellationToken);
        return setting is not null && string.Equals(setting.Key, key, StringComparison.Ordinal) ? setting : null;
    }
}

/// <summary>
/// The runtime reader's source: untracked, synchronous single-statement reads (see <see cref="ISettingsSnapshotSource"/>).
/// </summary>
public sealed class EfSettingsSnapshotSource(SettingsDbContext context) : ISettingsSnapshotSource
{
    /// <inheritdoc />
    public long ReadVersion() =>
        context.Stamps.AsNoTracking()
            .Where(stamp => stamp.Id == SettingsStamp.SingletonId)
            .Select(stamp => stamp.Version)
            .Single();

    /// <inheritdoc />
    public StoredSettings Load()
    {
        var version = ReadVersion();
        var values = context.Settings.AsNoTracking()
            .Select(setting => new { setting.Key, setting.ValueType, setting.Value })
            .ToDictionary(row => row.Key, row => new StoredSetting(row.ValueType, row.Value), StringComparer.Ordinal);
        return new StoredSettings(version, values);
    }
}
