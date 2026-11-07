using Codelab.Identity.Diagnostics;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

namespace Codelab.Identity.Oidc;

/// <summary>
/// The provider's JSON Web Key Set, fetched over the configured <see cref="IOidcBackChannel"/> (directly from
/// <see cref="OidcOptions.JwksUri"/> over the named client <see cref="OidcHttpClient.Name"/>, certificate validation on;
/// or through the integration gateway relay) and cached in this instance.
/// <para>
/// <b>Freshness.</b> A set younger than <see cref="CacheLifetime"/> is served as is. A token naming a <c>kid</c> the set
/// lacks forces one refetch (how a key rotation is picked up early), at most once per <see cref="UnknownKidRefreshInterval"/>,
/// so tokens with invented kids cannot make this host hammer the provider. A failed refresh keeps serving the old set up to
/// <see cref="MaxStaleness"/> and is not retried for <see cref="RefreshRetryBackoff"/>.
/// </para>
/// <para>
/// <b>Bounded.</b> Only asymmetric keys (<c>kty</c> RSA or EC, <c>use</c> absent or <c>sig</c>) are kept, a set with more
/// than <see cref="MaxKeys"/> keys is refused, and the response size is capped by the named client.
/// </para>
/// </summary>
public sealed class JwksSigningKeySource : IIdTokenSigningKeySource, IDisposable
{
    /// <summary>The soft lifetime of a fetched set.</summary>
    public static readonly TimeSpan CacheLifetime = TimeSpan.FromHours(1);

    /// <summary>The hard lifetime: a set that cannot be refreshed is not used beyond this age.</summary>
    public static readonly TimeSpan MaxStaleness = TimeSpan.FromHours(24);

    /// <summary>After a failed refresh, how long the stale set is served without another attempt.</summary>
    public static readonly TimeSpan RefreshRetryBackoff = TimeSpan.FromMinutes(2);

    /// <summary>The shortest interval between two refetches forced by an unknown <c>kid</c>.</summary>
    public static readonly TimeSpan UnknownKidRefreshInterval = TimeSpan.FromSeconds(30);

    /// <summary>The most keys a set may hold.</summary>
    public const int MaxKeys = 32;

    private const string SignatureUse = "sig";

    private readonly IOidcBackChannel backChannel;
    private readonly TimeProvider timeProvider;
    private readonly ILogger<JwksSigningKeySource> logger;
    private readonly SemaphoreSlim refresh = new(initialCount: 1, maxCount: 1);
    private CachedKeys? cached;
    private DateTimeOffset? lastForcedRefresh;

    /// <summary>A source over the configured <see cref="OidcOptions.JwksUri"/>, fetched directly over the named client.</summary>
    public JwksSigningKeySource(IHttpClientFactory clients, IOptions<OidcOptions> options, TimeProvider timeProvider, ILogger<JwksSigningKeySource> logger)
        : this(new DirectOidcBackChannel(clients, options), timeProvider, logger)
    {
    }

    /// <summary>A source over <paramref name="backChannel"/> (the one <c>AddCodelabOidc</c> registers uses the registered back channel).</summary>
    public JwksSigningKeySource(IOidcBackChannel backChannel, TimeProvider timeProvider, ILogger<JwksSigningKeySource> logger)
    {
        this.backChannel = backChannel ?? throw new ArgumentNullException(nameof(backChannel));
        this.timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));
        this.logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<SecurityKey>?> GetSigningKeysAsync(string? kid, CancellationToken cancellationToken)
    {
        if (Serveable(cached, kid, timeProvider.GetUtcNow()) is { } keys)
        {
            return keys;
        }

        await refresh.WaitAsync(cancellationToken);
        try
        {
            var now = timeProvider.GetUtcNow();
            return Serveable(cached, kid, now) ?? await RefreshAsync(kid, now, cancellationToken);
        }
        finally
        {
            refresh.Release();
        }
    }

    /// <summary>Releases the refresh gate.</summary>
    public void Dispose() => refresh.Dispose();

    /// <summary>The cached keys when they may be served without a fetch, otherwise null.</summary>
    private IReadOnlyList<SecurityKey>? Serveable(CachedKeys? entry, string? kid, DateTimeOffset now)
    {
        if (entry is null)
        {
            return null;
        }

        var age = now - entry.FetchedAt;
        if (!Knows(entry, kid))
        {
            // An unknown kid refetches, unless one did so a moment ago: then the set as it is answers (and fails validation).
            return RecentlyForced(now) && age < MaxStaleness ? entry.Keys : null;
        }

        var inBackoff = entry.RetryAfter is { } retryAfter && now < retryAfter && age < MaxStaleness;
        return age < CacheLifetime || inBackoff ? entry.Keys : null;
    }

    private bool RecentlyForced(DateTimeOffset now) => lastForcedRefresh is { } last && now - last < UnknownKidRefreshInterval;

    private static bool Knows(CachedKeys? entry, string? kid) =>
        string.IsNullOrEmpty(kid) || entry?.Keys.Any(key => string.Equals(key.KeyId, kid, StringComparison.Ordinal)) == true;

    private async Task<IReadOnlyList<SecurityKey>?> RefreshAsync(string? kid, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var previous = cached;
        if (previous is not null && !Knows(previous, kid))
        {
            lastForcedRefresh = now;
        }

        var fetched = await FetchAsync(cancellationToken);
        if (fetched is not null)
        {
            cached = new CachedKeys(fetched, FetchedAt: now, RetryAfter: null);
            return fetched;
        }

        if (previous is null || now - previous.FetchedAt >= MaxStaleness)
        {
            return null;
        }

        IdentityLog.OidcJwksServedStale(logger, previous.FetchedAt);
        cached = previous with { RetryAfter = now + RefreshRetryBackoff };
        return previous.Keys;
    }

    private async Task<IReadOnlyList<SecurityKey>?> FetchAsync(CancellationToken cancellationToken)
    {
        var response = await backChannel.GetJwksAsync(cancellationToken);
        if (response.Outcome != OidcBackChannelOutcome.Answered)
        {
            IdentityLog.OidcJwksFetchFailed(logger, response.FailureReason ?? response.Outcome.ToString());
            return null;
        }

        var status = response.StatusCode.GetValueOrDefault();
        if (status is < (int)System.Net.HttpStatusCode.OK or >= (int)System.Net.HttpStatusCode.MultipleChoices)
        {
            IdentityLog.OidcJwksFetchFailed(logger, string.Create(System.Globalization.CultureInfo.InvariantCulture, $"HTTP {status}"));
            return null;
        }

        try
        {
            return SigningKeysOf(response.Body ?? string.Empty);
        }
        catch (Exception failure) when (failure is ArgumentException or System.Text.Json.JsonException)
        {
            IdentityLog.OidcJwksFetchFailed(logger, failure.GetType().Name);
            return null;
        }
    }

    private IReadOnlyList<SecurityKey>? SigningKeysOf(string json)
    {
        var set = new JsonWebKeySet(json);
        if (set.Keys.Count > MaxKeys)
        {
            IdentityLog.OidcJwksFetchFailed(logger, $"the set holds more than {MaxKeys} keys");
            return null;
        }

        return [.. set.Keys.Where(IsAsymmetricSigningKey)];
    }

    private static bool IsAsymmetricSigningKey(JsonWebKey key) =>
        (string.IsNullOrEmpty(key.Use) || string.Equals(key.Use, SignatureUse, StringComparison.Ordinal))
        && (string.Equals(key.Kty, JsonWebAlgorithmsKeyTypes.RSA, StringComparison.Ordinal)
            || string.Equals(key.Kty, JsonWebAlgorithmsKeyTypes.EllipticCurve, StringComparison.Ordinal));

    private sealed record CachedKeys(IReadOnlyList<SecurityKey> Keys, DateTimeOffset FetchedAt, DateTimeOffset? RetryAfter);
}
