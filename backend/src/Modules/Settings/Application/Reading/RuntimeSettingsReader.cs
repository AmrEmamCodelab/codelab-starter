using Starter.Application.Settings;
using Starter.Modules.Settings.Domain;

namespace Starter.Modules.Settings.Application.Reading;

/// <summary>
/// The scoped <see cref="IRuntimeSettings"/>. Freshness contract: a snapshot is reloaded when there is none, when it is
/// <see cref="SettingsLimits.CacheTtl"/> old (checked on every read, by the injected <see cref="TimeProvider"/>), or when
/// the first read of the scope finds a different stamp version in the database (<see cref="SettingsReadPolicy"/>). So a
/// change on another node is seen by the next request, and at the latest after the TTL.
/// </summary>
public sealed class RuntimeSettingsReader(
    SettingsSnapshotCache cache,
    ISettingsSnapshotSource source,
    TimeProvider timeProvider,
    SettingsReadPolicy policy) : IRuntimeSettings
{
    private bool _stampChecked;

    /// <inheritdoc />
    public string GetString(string key) => Read(key, SettingValueType.Text).Value;

    /// <inheritdoc />
    public int GetInt32(string key) =>
        SettingValueFormat.TryReadInt32(Read(key, SettingValueType.WholeNumber).Value, out var value) ? value : throw Unreadable(key);

    /// <inheritdoc />
    public bool GetBoolean(string key) =>
        SettingValueFormat.TryReadBoolean(Read(key, SettingValueType.Boolean).Value, out var value) ? value : throw Unreadable(key);

    /// <inheritdoc />
    public TimeSpan GetDuration(string key) =>
        SettingValueFormat.TryReadDuration(Read(key, SettingValueType.DurationSeconds).Value, out var value) ? value : throw Unreadable(key);

    private static InvalidOperationException Unreadable(string key) =>
        new($"The stored value of runtime setting '{key}' does not match its type; correct it with a Settings migration.");

    private StoredSetting Read(string key, SettingValueType expected)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        var setting = Fresh().Find(key)
            ?? throw new InvalidOperationException($"No runtime setting has the key '{key}'; add it with a Settings migration.");
        return setting.ValueType == expected
            ? setting
            : throw new InvalidOperationException($"Runtime setting '{key}' is {setting.ValueType}, not {expected}.");
    }

    private SettingsSnapshot Fresh()
    {
        var now = timeProvider.GetUtcNow();
        var cached = cache.Current;
        if (cached is null || cached.IsExpiredAt(now))
        {
            return Reload(now);
        }

        if (_stampChecked || !policy.CheckStampPerScope)
        {
            return cached;
        }

        _stampChecked = true;
        return source.ReadVersion() == cached.Version ? cached : Reload(now);
    }

    private SettingsSnapshot Reload(DateTimeOffset now)
    {
        _stampChecked = true;
        return cache.Offer(SettingsSnapshot.From(source.Load(), now));
    }
}
