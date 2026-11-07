using Codelab.Gateway.Api.Diagnostics;
using Codelab.Gateway.Api.Relay;
using Microsoft.Extensions.Options;

namespace Codelab.Gateway.Api.Sso;

/// <summary>
/// The provider's JWKS document, cached in memory for <see cref="SsoRelayOptions.JwksCacheDuration"/> (freshness
/// contract: a document is served at most that long after it was fetched; zero disables the cache). One fetch at a
/// time: concurrent misses wait for the first one. Only a document that passed <see cref="SsoDocuments.IsJwks"/> is
/// cached; a failed refresh is answered as a failure (no stale copy is served, so a revoked key never outlives the
/// cache duration). Transient (it uses the transient upstream client); the entry lives in <see cref="JwksCacheStore"/>.
/// </summary>
internal sealed class JwksCache(ISsoUpstream upstream, JwksCacheStore store, IOptions<SsoRelayOptions> options, TimeProvider clock, ILogger<JwksCache> logger)
{
    /// <summary>The cached document while fresh, otherwise a freshly fetched one (or the fetch's failure).</summary>
    public async Task<UpstreamResult> GetAsync(CancellationToken cancellationToken)
    {
        if (store.FreshAt(clock.GetUtcNow()) is { } fresh)
        {
            return fresh;
        }

        await store.Refresh.WaitAsync(cancellationToken);
        try
        {
            return store.FreshAt(clock.GetUtcNow()) ?? await FetchAsync(cancellationToken);
        }
        finally
        {
            store.Refresh.Release();
        }
    }

    private async Task<UpstreamResult> FetchAsync(CancellationToken cancellationToken)
    {
        var result = await upstream.GetJwksAsync(cancellationToken);
        if (!result.Succeeded)
        {
            return result;
        }

        if (!SsoDocuments.IsJwks(result.Body))
        {
            return UpstreamResult.Failed(UpstreamFailureMapper.Unusable(result.Status!.Value));
        }

        var duration = options.Value.JwksCacheDuration;
        if (duration > TimeSpan.Zero)
        {
            store.Keep(result, expiresAt: clock.GetUtcNow() + duration);
            GatewayLog.JwksRefreshed(logger, duration);
        }

        return result;
    }
}

/// <summary>The singleton entry of <see cref="JwksCache"/>: the last good document, its expiry and the refresh gate.</summary>
internal sealed class JwksCacheStore : IDisposable
{
    private Entry? _entry;

    /// <summary>Serializes refreshes.</summary>
    public SemaphoreSlim Refresh { get; } = new(initialCount: 1, maxCount: 1);

    /// <summary>The cached document when it is still fresh at <paramref name="now"/>, otherwise null.</summary>
    public UpstreamResult? FreshAt(DateTimeOffset now)
    {
        var entry = Volatile.Read(ref _entry);
        return entry is not null && now < entry.ExpiresAt ? entry.Document : null;
    }

    /// <summary>Keeps <paramref name="document"/> until <paramref name="expiresAt"/>.</summary>
    public void Keep(UpstreamResult document, DateTimeOffset expiresAt) => Volatile.Write(ref _entry, new Entry(document, expiresAt));

    public void Dispose() => Refresh.Dispose();

    private sealed record Entry(UpstreamResult Document, DateTimeOffset ExpiresAt);
}
