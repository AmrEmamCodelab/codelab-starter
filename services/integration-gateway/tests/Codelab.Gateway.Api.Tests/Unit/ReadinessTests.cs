using Codelab.Gateway.Api.Diagnostics;
using Codelab.Gateway.Api.Relay;
using Codelab.Integration.Gateway;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace Codelab.Gateway.Api.Tests.Unit;

public sealed class UpstreamReadinessCheckTests
{
    private readonly FakeResolver _resolver = new();

    [Fact]
    public async Task Configured_upstreams_whose_hosts_resolve_are_ready()
    {
        var result = await CheckAsync(enabled: [Profile("sso", "https://idp.example.test/token")]);

        Assert.Equal(HealthStatus.Healthy, result.Status);
        Assert.Equal(["idp.example.test"], _resolver.Asked);
    }

    [Fact]
    public async Task No_enabled_profile_is_not_ready()
    {
        var result = await CheckAsync(enabled: []);

        Assert.Equal(HealthStatus.Unhealthy, result.Status);
        Assert.Equal(UpstreamReadinessCheck.NoProfileEnabled, result.Description);
    }

    [Fact]
    public async Task A_missing_upstream_address_names_the_profile_not_ready()
    {
        var result = await CheckAsync(enabled: [Profile("sms", address: null)]);

        Assert.Equal(HealthStatus.Unhealthy, result.Status);
        Assert.Contains("sms", result.Description, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_host_that_does_not_resolve_is_not_ready_and_the_host_is_not_named()
    {
        _resolver.Resolves = false;

        var result = await CheckAsync(enabled: [Profile("sso", "https://idp.example.test/token")]);

        Assert.Equal(HealthStatus.Unhealthy, result.Status);
        Assert.DoesNotContain("idp.example.test", result.Description, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_resolver_that_times_out_is_not_ready()
    {
        _resolver.Hang = true;

        var result = await CheckAsync(enabled: [Profile("sso", "https://idp.example.test/token")], timeout: TimeSpan.Zero);

        Assert.Equal(HealthStatus.Unhealthy, result.Status);
    }

    [Fact]
    public async Task Ip_addresses_and_a_switched_off_resolver_are_not_resolved()
    {
        await CheckAsync(enabled: [Profile("sso", "https://192.0.2.5/token")]);
        await CheckAsync(enabled: [Profile("sso", "https://idp.example.test/token")], resolve: false);

        Assert.Empty(_resolver.Asked);
    }

    private async Task<HealthCheckResult> CheckAsync(IReadOnlyList<IGatewayProfile> enabled, bool resolve = true, TimeSpan? timeout = null)
    {
        var gateway = Microsoft.Extensions.Options.Options.Create(new GatewayOptions());
        foreach (var profile in enabled)
        {
            gateway.Value.Profiles.Add(profile.Key);
        }

        var registry = new GatewayProfileRegistry(enabled, gateway);
        var readiness = Microsoft.Extensions.Options.Options.Create(new GatewayReadinessOptions { ResolveUpstreamHosts = resolve, ResolveTimeout = timeout ?? TimeSpan.FromSeconds(2) });
        var check = new UpstreamReadinessCheck(registry, _resolver, readiness, TimeProvider.System);
        return await check.CheckHealthAsync(new HealthCheckContext());
    }

    private static FakeProfile Profile(string key, string? address) => new FakeProfile(key, address is null ? null : new Uri(address));

    private sealed class FakeProfile(string key, Uri? address) : IGatewayProfile, IRelayUpstreams
    {
        public string Key => key;

        public IEnumerable<Uri?> UpstreamAddresses => [address];

        public void Map(IEndpointRouteBuilder endpoints)
        {
        }
    }

    private sealed class FakeResolver : IUpstreamHostResolver
    {
        public bool Resolves { get; set; } = true;

        public bool Hang { get; set; }

        public List<string> Asked { get; } = [];

        public async Task<bool> ResolvesAsync(string host, CancellationToken cancellationToken)
        {
            Asked.Add(host);
            if (Hang)
            {
                await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            }

            return Resolves;
        }
    }
}

public sealed class DnsUpstreamHostResolverTests
{
    [Fact]
    public async Task Localhost_resolves() => Assert.True(await new DnsUpstreamHostResolver().ResolvesAsync("localhost", CancellationToken.None));

    [Fact]
    public async Task A_reserved_invalid_name_does_not_resolve() =>
        Assert.False(await new DnsUpstreamHostResolver().ResolvesAsync("no-such-host.invalid", CancellationToken.None));
}
