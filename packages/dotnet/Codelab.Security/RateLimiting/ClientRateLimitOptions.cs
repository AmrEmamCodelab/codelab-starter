using System.Net;
using Microsoft.Extensions.Options;

namespace Codelab.Security.RateLimiting;

/// <summary>
/// Client rate limiting (<c>Codelab:Security:RateLimiting</c>): a fixed window per client address, where the client
/// address comes from <c>X-Forwarded-For</c> ONLY when the connection arrives from a listed trusted proxy.
/// </summary>
public sealed class ClientRateLimitOptions
{
    /// <summary>The configuration section.</summary>
    public const string SectionName = SecuritySections.RateLimiting;

    /// <summary>The named policy endpoints opt into with <c>RequireRateLimiting(ClientRateLimitOptions.PolicyName)</c>.</summary>
    public const string PolicyName = "codelab-client";

    /// <summary>The forwarded-for header.</summary>
    public const string ForwardedForHeader = "X-Forwarded-For";

    /// <summary>Requests allowed per client per window.</summary>
    public int PermitLimit { get; set; } = 100;

    /// <summary>The window length in seconds.</summary>
    public int WindowSeconds { get; set; } = 60;

    /// <summary>Apply the per-client limit to every endpoint (default off: endpoints opt in by policy name).</summary>
    public bool ApplyGlobally { get; set; }

    /// <summary>
    /// The reverse proxies and load balancers whose <c>X-Forwarded-For</c> is believed: single addresses
    /// (<c>192.0.2.5</c>) or CIDR ranges (<c>192.0.2.0/24</c>). Empty (default): the header is ignored and the
    /// connection address is the client, so a client cannot pick its own partition by sending the header.
    /// </summary>
    public IList<string> TrustedProxies { get; init; } = [];

    /// <summary>The parsed trusted proxies; throws on an entry that is neither an address nor a CIDR range.</summary>
    public IReadOnlyList<IPNetwork> ParseTrustedProxies() => TrustedProxies.Select(ParseProxy).ToList();

    private const int BitsPerByte = 8;

    private static IPNetwork ParseProxy(string entry) =>
        IPNetwork.TryParse(entry, out var network) ? network
        : IPAddress.TryParse(entry, out var address) ? SingleAddress(Normalize(address))
        : throw new FormatException($"{SectionName}:TrustedProxies entry '{entry}' is neither an IP address nor a CIDR range.");

    private static IPNetwork SingleAddress(IPAddress address) => new(address, address.GetAddressBytes().Length * BitsPerByte);

    internal static IPAddress Normalize(IPAddress address) => address.IsIPv4MappedToIPv6 ? address.MapToIPv4() : address;
}

/// <summary>Validates <see cref="ClientRateLimitOptions"/> at start.</summary>
internal sealed class ClientRateLimitOptionsValidator : IValidateOptions<ClientRateLimitOptions>
{
    public ValidateOptionsResult Validate(string? name, ClientRateLimitOptions options)
    {
        if (options.PermitLimit <= 0 || options.WindowSeconds <= 0)
        {
            return ValidateOptionsResult.Fail($"{ClientRateLimitOptions.SectionName}: PermitLimit and WindowSeconds must be positive.");
        }

        try
        {
            options.ParseTrustedProxies();
            return ValidateOptionsResult.Success;
        }
        catch (FormatException ex)
        {
            return ValidateOptionsResult.Fail(ex.Message);
        }
    }
}
