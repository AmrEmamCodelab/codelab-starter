using System.Net;
using System.Threading.RateLimiting;
using Microsoft.Extensions.Options;

namespace Codelab.Security.RateLimiting;

/// <summary>
/// Resolves the client address for rate limiting. When the connection comes from a trusted proxy, the
/// <c>X-Forwarded-For</c> chain is walked from the right (the entry the nearest proxy appended) past every trusted
/// proxy; the first untrusted address is the client. Entries left of it were written by the client and are never
/// believed. A malformed entry stops the walk at the last trusted hop. Without a trusted proxy the header is ignored.
/// </summary>
public sealed class ClientAddressResolver(IOptionsMonitor<ClientRateLimitOptions> options)
{
    private const char ListSeparator = ',';

    /// <summary>The client address, or null when the connection has none (in-process test hosts).</summary>
    public IPAddress? Resolve(HttpContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        if (context.Connection.RemoteIpAddress is not { } connection)
        {
            return null;
        }

        var remote = ClientRateLimitOptions.Normalize(connection);
        var trusted = options.CurrentValue.ParseTrustedProxies();
        return IsTrusted(remote, trusted) ? WalkForwardedChain(context.Request.Headers[ClientRateLimitOptions.ForwardedForHeader], remote, trusted) : remote;
    }

    /// <summary>The partition for <paramref name="context"/>: a fixed window keyed by the resolved client address.</summary>
    public RateLimitPartition<string> PartitionFor(HttpContext context)
    {
        var settings = options.CurrentValue;
        var key = Resolve(context)?.ToString() ?? string.Empty;
        return RateLimitPartition.GetFixedWindowLimiter(key, _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = settings.PermitLimit,
            Window = TimeSpan.FromSeconds(settings.WindowSeconds),
            QueueLimit = 0,
        });
    }

    private static IPAddress WalkForwardedChain(IEnumerable<string?> headerValues, IPAddress remote, IReadOnlyList<IPNetwork> trusted)
    {
        var hops = headerValues
            .SelectMany(value => (value ?? string.Empty).Split(ListSeparator, StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
            .Reverse();
        var client = remote;
        foreach (var hop in hops)
        {
            if (!IPAddress.TryParse(hop, out var parsed))
            {
                return client;
            }

            client = ClientRateLimitOptions.Normalize(parsed);
            if (!IsTrusted(client, trusted))
            {
                return client;
            }
        }

        return client;
    }

    private static bool IsTrusted(IPAddress address, IReadOnlyList<IPNetwork> trusted) => trusted.Any(network => network.Contains(address));
}
