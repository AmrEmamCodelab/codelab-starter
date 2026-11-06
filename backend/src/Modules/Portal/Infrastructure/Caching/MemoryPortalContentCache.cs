using System.Globalization;
using Microsoft.Extensions.Caching.Memory;
using Starter.Modules.Portal.Application;
using Starter.Modules.Portal.Application.Content;
using Starter.Modules.Portal.Domain;

namespace Starter.Modules.Portal.Infrastructure.Caching;

/// <summary>
/// The <see cref="IPortalContentCache"/> on the host's <see cref="IMemoryCache"/>. Each entry records when it expires by
/// the injected <see cref="TimeProvider"/> (so the TTL is exact and testable), and the memory cache evicts it after the
/// same <see cref="PortalLimits.ContentCacheTtl"/>, so it never outlives it. The cached views are immutable.
/// </summary>
public sealed class MemoryPortalContentCache(IMemoryCache memoryCache, TimeProvider timeProvider) : IPortalContentCache
{
    /// <summary>The cache key prefix of a page's public content (<c>portal:content:&lt;page int&gt;</c>).</summary>
    public const string KeyPrefix = "portal:content:";

    /// <summary>The size of one entry, for a host that sets a memory cache size limit.</summary>
    private const long EntrySize = 1;

    /// <inheritdoc />
    public PortalContentView? Find(PortalPage page)
    {
        if (!memoryCache.TryGetValue(KeyOf(page), out CachedContent? entry) || entry is null)
        {
            return null;
        }

        if (timeProvider.GetUtcNow() < entry.ExpiresAt)
        {
            return entry.Content;
        }

        memoryCache.Remove(KeyOf(page));
        return null;
    }

    /// <inheritdoc />
    public void Store(PortalContentView content)
    {
        ArgumentNullException.ThrowIfNull(content);
        var entry = new CachedContent(content, timeProvider.GetUtcNow() + PortalLimits.ContentCacheTtl);
        memoryCache.Set(KeyOf(content.Page), entry, new MemoryCacheEntryOptions
        {
            AbsoluteExpirationRelativeToNow = PortalLimits.ContentCacheTtl,
            Size = EntrySize,
        });
    }

    /// <inheritdoc />
    public void Invalidate(PortalPage page) => memoryCache.Remove(KeyOf(page));

    /// <summary>The cache key of <paramref name="page"/>'s public content.</summary>
    public static string KeyOf(PortalPage page) => KeyPrefix + ((int)page).ToString(CultureInfo.InvariantCulture);

    private sealed record CachedContent(PortalContentView Content, DateTimeOffset ExpiresAt);
}
