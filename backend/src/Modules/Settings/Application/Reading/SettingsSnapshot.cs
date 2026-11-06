using Starter.Application.Settings;

namespace Starter.Modules.Settings.Application.Reading;

/// <summary>An immutable copy of every setting value at one stamp version, as one node holds it in memory.</summary>
public sealed class SettingsSnapshot
{
    private readonly IReadOnlyDictionary<string, StoredSetting> _values;

    private SettingsSnapshot(long version, DateTimeOffset loadedAt, IReadOnlyDictionary<string, StoredSetting> values)
    {
        Version = version;
        LoadedAt = loadedAt;
        _values = values;
    }

    /// <summary>The stamp version the values were loaded at.</summary>
    public long Version { get; }

    /// <summary>When this node loaded it (from the injected <see cref="TimeProvider"/>).</summary>
    public DateTimeOffset LoadedAt { get; }

    /// <summary>A snapshot of <paramref name="stored"/>, loaded at <paramref name="loadedAt"/>.</summary>
    public static SettingsSnapshot From(StoredSettings stored, DateTimeOffset loadedAt)
    {
        ArgumentNullException.ThrowIfNull(stored);
        var values = new Dictionary<string, StoredSetting>(stored.Values, StringComparer.Ordinal);
        return new SettingsSnapshot(stored.Version, loadedAt, values);
    }

    /// <summary>
    /// True when the snapshot is <see cref="SettingsLimits.CacheTtl"/> old or older at <paramref name="now"/>, so it
    /// must be reloaded before the next read (exactly 30 s old is expired).
    /// </summary>
    public bool IsExpiredAt(DateTimeOffset now) => now - LoadedAt >= SettingsLimits.CacheTtl;

    /// <summary>The setting with this key, or null.</summary>
    public StoredSetting? Find(string key) => _values.GetValueOrDefault(key);
}
