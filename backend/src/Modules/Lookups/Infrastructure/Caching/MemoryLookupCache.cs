using Microsoft.Extensions.Caching.Memory;
using Starter.Modules.Lookups.Application;

namespace Starter.Modules.Lookups.Infrastructure.Caching;

/// <summary>
/// The <see cref="ILookupCache"/> on the host's <see cref="IMemoryCache"/>. Each entry records when it expires by the
/// injected <see cref="TimeProvider"/> (so the TTL is exact and testable), and the memory cache evicts it after the same
/// <see cref="LookupsLimits.CacheTtl"/>, so it never outlives it. The cached lists are immutable snapshots.
/// </summary>
public sealed class MemoryLookupCache(IMemoryCache memoryCache, TimeProvider timeProvider) : ILookupCache
{
    /// <summary>The cache key prefix of a type's active list (<c>lookups:active:&lt;type&gt;</c>).</summary>
    public const string KeyPrefix = "lookups:active:";

    /// <summary>The size of one entry, for a host that sets a memory cache size limit.</summary>
    private const long EntrySize = 1;

    /// <inheritdoc />
    public IReadOnlyList<LookupValue>? Find(string typeKey)
    {
        if (!memoryCache.TryGetValue(KeyOf(typeKey), out CachedValues? entry) || entry is null)
        {
            return null;
        }

        if (timeProvider.GetUtcNow() < entry.ExpiresAt)
        {
            return entry.Values;
        }

        memoryCache.Remove(KeyOf(typeKey));
        return null;
    }

    /// <inheritdoc />
    public void Store(string typeKey, IReadOnlyList<LookupValue> values)
    {
        ArgumentNullException.ThrowIfNull(values);
        var entry = new CachedValues([.. values], timeProvider.GetUtcNow() + LookupsLimits.CacheTtl);
        memoryCache.Set(KeyOf(typeKey), entry, new MemoryCacheEntryOptions
        {
            AbsoluteExpirationRelativeToNow = LookupsLimits.CacheTtl,
            Size = EntrySize,
        });
    }

    /// <inheritdoc />
    public void Invalidate(string typeKey) => memoryCache.Remove(KeyOf(typeKey));

    /// <summary>The cache key of <paramref name="typeKey"/>'s active list.</summary>
    public static string KeyOf(string typeKey)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(typeKey);
        return KeyPrefix + typeKey;
    }

    private sealed record CachedValues(IReadOnlyList<LookupValue> Values, DateTimeOffset ExpiresAt);
}
