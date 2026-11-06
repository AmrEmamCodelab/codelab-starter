using Codelab.Security.PublicIds;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Time.Testing;
using Starter.Domain.Lookups;
using Starter.Modules.Lookups.Application;
using Starter.Modules.Lookups.Infrastructure.Caching;
using Starter.Modules.Lookups.Tests.Fakes;

namespace Starter.Modules.Lookups.Tests.Infrastructure;

public sealed class MemoryLookupCacheTests : IDisposable
{
    private static readonly IReadOnlyList<LookupValue> Values = [new(ContactTopicIds.General, "general", "عام", "General", 10)];

    private readonly FakeTimeProvider _time = new(new DateTimeOffset(2026, 11, 6, 9, 0, 0, TimeSpan.Zero));
    private readonly MemoryCache _memory = new(new MemoryCacheOptions());

    [Fact]
    public void A_stored_list_is_found_until_the_ttl_elapses()
    {
        var cache = new MemoryLookupCache(_memory, _time);

        cache.Store(LookupTypes.ContactTopics, Values);
        _time.Advance(LookupsLimits.CacheTtl - TimeSpan.FromSeconds(1));
        Assert.Equal(Values, cache.Find(LookupTypes.ContactTopics));

        _time.Advance(TimeSpan.FromSeconds(1));
        Assert.Null(cache.Find(LookupTypes.ContactTopics));
    }

    [Fact]
    public void Invalidate_drops_only_that_type()
    {
        var cache = new MemoryLookupCache(_memory, _time);
        cache.Store(LookupTypes.ContactTopics, Values);
        cache.Store("other-type", Values);

        cache.Invalidate(LookupTypes.ContactTopics);

        Assert.Null(cache.Find(LookupTypes.ContactTopics));
        Assert.NotNull(cache.Find("other-type"));
    }

    [Fact]
    public void The_cached_list_is_a_snapshot()
    {
        var cache = new MemoryLookupCache(_memory, _time);
        var source = new List<LookupValue>(Values);

        cache.Store(LookupTypes.ContactTopics, source);
        source.Clear();

        Assert.Single(cache.Find(LookupTypes.ContactTopics)!);
    }

    [Fact]
    public void A_missing_entry_is_null_and_keys_are_prefixed()
    {
        Assert.Null(new MemoryLookupCache(_memory, _time).Find(LookupTypes.ContactTopics));
        Assert.Equal("lookups:active:contact-topics", MemoryLookupCache.KeyOf(LookupTypes.ContactTopics));
    }

    [Fact]
    public void A_host_with_a_size_limit_still_accepts_entries()
    {
        using var limited = new MemoryCache(new MemoryCacheOptions { SizeLimit = 10 });
        var cache = new MemoryLookupCache(limited, _time);

        cache.Store(LookupTypes.ContactTopics, Values);

        Assert.NotNull(cache.Find(LookupTypes.ContactTopics));
    }

    public void Dispose() => _memory.Dispose();
}

/// <summary>The read and the activation change over the real cache, on one node and across two nodes.</summary>
public sealed class CacheInvalidationTests : IDisposable
{
    private readonly FakeTimeProvider _time = new(new DateTimeOffset(2026, 11, 6, 9, 0, 0, TimeSpan.Zero));
    private readonly FakeLookupRepository _database = FakeLookupRepository.Seeded();
    private readonly MemoryCache _nodeAMemory = new(new MemoryCacheOptions());
    private readonly MemoryCache _nodeBMemory = new(new MemoryCacheOptions());

    [Fact]
    public async Task A_deactivation_is_visible_at_once_on_the_node_that_made_it()
    {
        var cache = new MemoryLookupCache(_nodeAMemory, _time);
        var read = new GetActiveLookupValues(_database, cache);
        Assert.Equal(4, (await read.ExecuteAsync(LookupTypes.ContactTopics, CancellationToken.None)).Value.Count);

        await Deactivate(cache, ContactTopicIds.Request);

        var after = await read.ExecuteAsync(LookupTypes.ContactTopics, CancellationToken.None);
        Assert.DoesNotContain(after.Value, value => value.Id == ContactTopicIds.Request);
    }

    [Fact]
    public async Task Another_node_sees_the_change_within_the_ttl()
    {
        var nodeA = new MemoryLookupCache(_nodeAMemory, _time);
        var nodeB = new MemoryLookupCache(_nodeBMemory, _time);
        var readOnB = new GetActiveLookupValues(_database, nodeB);
        await readOnB.ExecuteAsync(LookupTypes.ContactTopics, CancellationToken.None);

        await Deactivate(nodeA, ContactTopicIds.Request);
        _time.Advance(LookupsLimits.CacheTtl - TimeSpan.FromSeconds(1));
        var stale = await readOnB.ExecuteAsync(LookupTypes.ContactTopics, CancellationToken.None);
        _time.Advance(TimeSpan.FromSeconds(1));
        var fresh = await readOnB.ExecuteAsync(LookupTypes.ContactTopics, CancellationToken.None);

        Assert.Contains(stale.Value, value => value.Id == ContactTopicIds.Request);
        Assert.DoesNotContain(fresh.Value, value => value.Id == ContactTopicIds.Request);
    }

    public void Dispose()
    {
        _nodeAMemory.Dispose();
        _nodeBMemory.Dispose();
    }

    private async Task Deactivate(ILookupCache cache, int id)
    {
        var change = new SetLookupItemActive(new SetLookupItemActiveCommandValidator(), _database, new FakeUnitOfWork(), cache);
        var outcome = await change.ExecuteAsync(LookupTypes.ContactTopics, PublicId<LookupItemIds>.From(id), new(IsActive: false), CancellationToken.None);
        Assert.True(outcome.IsSuccess);
    }
}
