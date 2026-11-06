using Starter.Application.Settings;
using Starter.Modules.Settings.Application;
using Starter.Modules.Settings.Application.Reading;

namespace Starter.Modules.Settings.Tests.Application;

public sealed class SettingsSnapshotCacheTests
{
    private static readonly DateTimeOffset Loaded = new(2026, 11, 6, 9, 0, 0, TimeSpan.Zero);

    [Fact]
    public void It_starts_empty_and_holds_what_was_offered()
    {
        var cache = new SettingsSnapshotCache();
        Assert.Null(cache.Current);

        var snapshot = Snapshot(version: 3);

        Assert.Same(snapshot, cache.Offer(snapshot));
        Assert.Same(snapshot, cache.Current);
    }

    [Fact]
    public void A_slow_load_with_an_older_version_never_replaces_a_newer_snapshot()
    {
        var cache = new SettingsSnapshotCache();
        var newer = cache.Offer(Snapshot(version: 5));

        Assert.Same(newer, cache.Offer(Snapshot(version: 4)));
        Assert.Same(newer, cache.Current);
    }

    [Fact]
    public void A_reload_at_the_same_version_replaces_the_snapshot_so_its_age_restarts()
    {
        var cache = new SettingsSnapshotCache();
        cache.Offer(Snapshot(version: 5));
        var reloaded = Snapshot(version: 5, loadedAt: Loaded + SettingsLimits.CacheTtl);

        Assert.Same(reloaded, cache.Offer(reloaded));
    }

    [Fact]
    public void Invalidate_drops_the_snapshot()
    {
        var cache = new SettingsSnapshotCache();
        cache.Offer(Snapshot(version: 1));

        cache.Invalidate();

        Assert.Null(cache.Current);
    }

    [Fact]
    public void A_snapshot_expires_at_exactly_the_ttl()
    {
        var snapshot = Snapshot(version: 1);

        Assert.False(snapshot.IsExpiredAt(Loaded + SettingsLimits.CacheTtl - TimeSpan.FromTicks(1)));
        Assert.True(snapshot.IsExpiredAt(Loaded + SettingsLimits.CacheTtl));
        Assert.Equal(TimeSpan.FromSeconds(30), SettingsLimits.CacheTtl);
    }

    [Fact]
    public void A_snapshot_is_a_copy_and_finds_by_ordinal_key()
    {
        var values = new Dictionary<string, StoredSetting>(StringComparer.Ordinal) { ["a.b"] = new(SettingValueType.Text, "x") };
        var snapshot = SettingsSnapshot.From(new StoredSettings(Version: 1, values), Loaded);
        values["a.b"] = new(SettingValueType.Text, "changed");

        Assert.Equal("x", snapshot.Find("a.b")!.Value);
        Assert.Null(snapshot.Find("A.B"));
    }

    private static SettingsSnapshot Snapshot(long version, DateTimeOffset? loadedAt = null) =>
        SettingsSnapshot.From(new StoredSettings(version, new Dictionary<string, StoredSetting>(StringComparer.Ordinal)), loadedAt ?? Loaded);
}
