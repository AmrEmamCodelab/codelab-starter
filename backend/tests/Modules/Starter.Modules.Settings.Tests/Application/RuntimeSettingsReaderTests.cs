using Microsoft.Extensions.Time.Testing;
using Starter.Application.Settings;
using Starter.Modules.Settings.Application.Reading;
using Starter.Modules.Settings.Tests.Fakes;

namespace Starter.Modules.Settings.Tests.Application;

public sealed class RuntimeSettingsReaderTests
{
    private static readonly SettingsReadPolicy CheckStamp = new(CheckStampPerScope: true);
    private static readonly SettingsReadPolicy TtlOnly = new(CheckStampPerScope: false);
    private static readonly TimeSpan JustUnderTtl = SettingsLimits.CacheTtl - TimeSpan.FromMilliseconds(1);

    private readonly FakeSettingsStore _store = new();
    private readonly FakeTimeProvider _clock = new(new DateTimeOffset(2026, 11, 6, 9, 0, 0, TimeSpan.Zero));
    private readonly SettingsSnapshotCache _cache = new();

    [Fact]
    public void Each_getter_reads_its_type()
    {
        var reader = Scope(CheckStamp);

        Assert.Equal("Welcome", reader.GetString(SampleSettings.TextKey));
        Assert.Equal(20, reader.GetInt32(SampleSettings.IntegerKey));
        Assert.True(reader.GetBoolean(SampleSettings.BooleanKey));
        Assert.Equal(TimeSpan.FromMinutes(5), reader.GetDuration(SampleSettings.DurationKey));
    }

    [Fact]
    public void The_first_read_loads_once_and_the_rest_of_the_scope_queries_nothing()
    {
        var reader = Scope(CheckStamp);

        reader.GetInt32(SampleSettings.IntegerKey);
        reader.GetBoolean(SampleSettings.BooleanKey);
        reader.GetString(SampleSettings.TextKey);

        Assert.Equal(1, _store.Loads);
        Assert.Equal(0, _store.VersionReads); // a cold load reads the stamp inside Load
    }

    [Fact]
    public void A_new_scope_checks_the_stamp_once_and_keeps_the_snapshot_when_it_is_unchanged()
    {
        Scope(CheckStamp).GetInt32(SampleSettings.IntegerKey);

        var next = Scope(CheckStamp);
        next.GetInt32(SampleSettings.IntegerKey);
        next.GetInt32(SampleSettings.IntegerKey);

        Assert.Equal(1, _store.Loads);
        Assert.Equal(1, _store.VersionReads);
    }

    [Fact]
    public void A_changed_stamp_reloads_on_the_next_scope()
    {
        Scope(CheckStamp).GetInt32(SampleSettings.IntegerKey);
        _store.Change(SampleSettings.IntegerKey, "50");

        Assert.Equal(50, Scope(CheckStamp).GetInt32(SampleSettings.IntegerKey));
        Assert.Equal(2, _store.Loads);
    }

    [Fact]
    public void A_scope_that_already_checked_the_stamp_does_not_check_again_before_the_ttl()
    {
        var reader = Scope(CheckStamp);
        reader.GetInt32(SampleSettings.IntegerKey);
        _store.Change(SampleSettings.IntegerKey, "50");
        _clock.Advance(JustUnderTtl);

        Assert.Equal(20, reader.GetInt32(SampleSettings.IntegerKey));
    }

    [Fact]
    public void Without_the_stamp_check_the_snapshot_holds_until_just_under_the_ttl()
    {
        Scope(TtlOnly).GetInt32(SampleSettings.IntegerKey);
        _store.Change(SampleSettings.IntegerKey, "50");
        _clock.Advance(JustUnderTtl);

        Assert.Equal(20, Scope(TtlOnly).GetInt32(SampleSettings.IntegerKey));
        Assert.Equal(0, _store.VersionReads);
    }

    [Fact]
    public void Without_the_stamp_check_the_snapshot_reloads_at_exactly_the_ttl()
    {
        Scope(TtlOnly).GetInt32(SampleSettings.IntegerKey);
        _store.Change(SampleSettings.IntegerKey, "50");
        _clock.Advance(SettingsLimits.CacheTtl);

        Assert.Equal(50, Scope(TtlOnly).GetInt32(SampleSettings.IntegerKey));
        Assert.Equal(2, _store.Loads);
    }

    [Fact]
    public void A_long_lived_scope_reloads_at_the_ttl_even_after_its_stamp_check()
    {
        var reader = Scope(CheckStamp);
        reader.GetInt32(SampleSettings.IntegerKey);
        _store.ChangeWithoutStamp(SampleSettings.IntegerKey, "50");
        _clock.Advance(SettingsLimits.CacheTtl);

        Assert.Equal(50, reader.GetInt32(SampleSettings.IntegerKey));
    }

    [Fact]
    public void A_change_on_node_A_is_visible_on_node_B_within_the_ttl()
    {
        var nodeBCache = new SettingsSnapshotCache();
        Assert.Equal(20, new RuntimeSettingsReader(nodeBCache, _store, _clock, TtlOnly).GetInt32(SampleSettings.IntegerKey));

        _store.Change(SampleSettings.IntegerKey, "50"); // node A commits value + stamp
        _clock.Advance(SettingsLimits.CacheTtl);

        Assert.Equal(50, new RuntimeSettingsReader(nodeBCache, _store, _clock, TtlOnly).GetInt32(SampleSettings.IntegerKey));
    }

    [Fact]
    public void An_invalidated_cache_reloads_on_the_next_read()
    {
        Scope(CheckStamp).GetInt32(SampleSettings.IntegerKey);
        _cache.Invalidate();

        Scope(CheckStamp).GetInt32(SampleSettings.IntegerKey);

        Assert.Equal(2, _store.Loads);
    }

    [Fact]
    public void An_unknown_key_is_a_programming_error()
    {
        var error = Assert.Throws<InvalidOperationException>(() => Scope(CheckStamp).GetString("nope.missing"));

        Assert.Contains("nope.missing", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Reading_a_setting_as_another_type_is_a_programming_error()
    {
        var reader = Scope(CheckStamp);

        Assert.Throws<InvalidOperationException>(() => reader.GetString(SampleSettings.IntegerKey));
        Assert.Throws<InvalidOperationException>(() => reader.GetInt32(SampleSettings.BooleanKey));
        Assert.Throws<InvalidOperationException>(() => reader.GetBoolean(SampleSettings.DurationKey));
        Assert.Throws<InvalidOperationException>(() => reader.GetDuration(SampleSettings.TextKey));
    }

    [Theory]
    [InlineData(SampleSettings.IntegerKey)]
    [InlineData(SampleSettings.BooleanKey)]
    [InlineData(SampleSettings.DurationKey)]
    public void A_stored_value_that_does_not_parse_is_reported_not_guessed(string key)
    {
        _store.ChangeWithoutStamp(key, "garbage");
        var reader = Scope(CheckStamp);

        Assert.Throws<InvalidOperationException>(() => _ = key switch
        {
            SampleSettings.IntegerKey => (object)reader.GetInt32(key),
            SampleSettings.BooleanKey => reader.GetBoolean(key),
            _ => reader.GetDuration(key),
        });
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    public void A_blank_key_is_refused(string key) =>
        Assert.Throws<ArgumentException>(() => Scope(CheckStamp).GetString(key));

    private RuntimeSettingsReader Scope(SettingsReadPolicy policy) => new(_cache, _store, _clock, policy);
}
