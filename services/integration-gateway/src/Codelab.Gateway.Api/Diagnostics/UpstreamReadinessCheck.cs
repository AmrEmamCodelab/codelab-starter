using System.Net;
using System.Net.Sockets;
using Codelab.Gateway.Api.Relay;
using Codelab.Integration.Gateway;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;

namespace Codelab.Gateway.Api.Diagnostics;

/// <summary>The readiness settings (<see cref="SectionName"/>).</summary>
internal sealed class GatewayReadinessOptions
{
    /// <summary>The configuration section.</summary>
    public const string SectionName = "Gateway:Readiness";

    /// <summary>
    /// Resolves the host name of every enabled upstream (DNS only, no request is sent). Turn it off when the DMZ
    /// reaches the upstreams through a proxy that resolves names itself.
    /// </summary>
    public bool ResolveUpstreamHosts { get; set; } = true;

    /// <summary>How long one host name may take to resolve before the node reports not ready.</summary>
    public TimeSpan ResolveTimeout { get; set; } = TimeSpan.FromSeconds(2);
}

/// <summary>Resolves upstream host names (a port, so readiness is testable without DNS).</summary>
internal interface IUpstreamHostResolver
{
    /// <summary>True when <paramref name="host"/> resolves to at least one address.</summary>
    Task<bool> ResolvesAsync(string host, CancellationToken cancellationToken);
}

/// <summary>The system resolver.</summary>
internal sealed class DnsUpstreamHostResolver : IUpstreamHostResolver
{
    public async Task<bool> ResolvesAsync(string host, CancellationToken cancellationToken)
    {
        try
        {
            return (await Dns.GetHostAddressesAsync(host, cancellationToken)).Length > 0;
        }
        catch (SocketException)
        {
            return false;
        }
    }
}

/// <summary>
/// Readiness (<c>/health/ready</c>): at least one profile is enabled, every enabled profile has its upstream
/// addresses configured, and (unless turned off) every upstream host name resolves. It never sends a request to an
/// upstream: a provider outage shows in the relay's 503 answers, not as a node leaving the load balancer. The
/// description names the profile, never the host.
/// </summary>
internal sealed class UpstreamReadinessCheck(
    GatewayProfileRegistry registry,
    IUpstreamHostResolver resolver,
    IOptions<GatewayReadinessOptions> options,
    TimeProvider clock) : IHealthCheck
{
    internal const string NoProfileEnabled = "No relay profile is enabled.";

    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        if (registry.Enabled.Count == 0)
        {
            return HealthCheckResult.Unhealthy(NoProfileEnabled);
        }

        foreach (var profile in registry.Enabled)
        {
            if (await ProblemOfAsync(profile, cancellationToken) is { } problem)
            {
                return HealthCheckResult.Unhealthy(problem);
            }
        }

        return HealthCheckResult.Healthy();
    }

    private async Task<string?> ProblemOfAsync(IGatewayProfile profile, CancellationToken cancellationToken)
    {
        if (profile is not IRelayUpstreams upstreams)
        {
            return null;
        }

        foreach (var address in upstreams.UpstreamAddresses)
        {
            if (address is null || !address.IsAbsoluteUri)
            {
                return $"The {profile.Key} profile has no upstream address configured.";
            }

            if (!await ResolvesAsync(address, cancellationToken))
            {
                return $"An upstream host of the {profile.Key} profile does not resolve.";
            }
        }

        return null;
    }

    private async Task<bool> ResolvesAsync(Uri address, CancellationToken cancellationToken)
    {
        if (!options.Value.ResolveUpstreamHosts || address.HostNameType is UriHostNameType.IPv4 or UriHostNameType.IPv6)
        {
            return true;
        }

        using var timeout = new CancellationTokenSource(options.Value.ResolveTimeout, clock);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeout.Token);
        try
        {
            return await resolver.ResolvesAsync(address.DnsSafeHost, linked.Token);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return false;
        }
    }
}
