using Starter.Application.Persistence;
using Starter.Application.Settings;
using Starter.Modules.Settings.Domain;

namespace Starter.Modules.Settings.Application;

/// <summary>The Settings module's persistence boundary (implemented by its EF Core context in Infrastructure).</summary>
public interface ISettingsUnitOfWork : IUnitOfWork
{
    /// <summary>
    /// Writes every staged setting change and increments the <see cref="SettingsStamp"/> version in ONE transaction, so
    /// no node can see the new value without the new stamp (or the reverse).
    /// </summary>
    /// <exception cref="InvalidOperationException">The stamp row is missing (the InitialCreate migration seeds it).</exception>
    Task SaveChangesAndAdvanceStampAsync(CancellationToken cancellationToken);
}

/// <summary>The settings, for the admin use cases (tracked: a found setting can be changed and saved).</summary>
public interface ISettingsRepository
{
    /// <summary>Every setting, ordered by group, then key.</summary>
    Task<IReadOnlyList<Setting>> ListAsync(CancellationToken cancellationToken);

    /// <summary>The setting with this key (ordinal, case-sensitive), or null.</summary>
    Task<Setting?> FindAsync(string key, CancellationToken cancellationToken);
}

/// <summary>
/// What the runtime reader loads: the stamp version (cheap, one row) or the whole value set with its version. Synchronous
/// on purpose: <see cref="IRuntimeSettings"/> is a synchronous port and calls this at most once per scope and per
/// <see cref="SettingsLimits.CacheTtl"/>.
/// </summary>
public interface ISettingsSnapshotSource
{
    /// <summary>The current stamp version.</summary>
    long ReadVersion();

    /// <summary>
    /// The stamp version, then every value. The version is read FIRST, so a change that lands in between leaves the
    /// snapshot with an older version than its data and the next stamp check reloads it (never the reverse).
    /// </summary>
    StoredSettings Load();
}

/// <summary>One setting's type and canonical value, as the reader keeps it.</summary>
/// <param name="ValueType">How the value is read.</param>
/// <param name="Value">The canonical stored value.</param>
public sealed record StoredSetting(SettingValueType ValueType, string Value);

/// <summary>Every setting's value at one stamp version.</summary>
/// <param name="Version">The stamp version read before the values.</param>
/// <param name="Values">The values by key (ordinal).</param>
public sealed record StoredSettings(long Version, IReadOnlyDictionary<string, StoredSetting> Values);
