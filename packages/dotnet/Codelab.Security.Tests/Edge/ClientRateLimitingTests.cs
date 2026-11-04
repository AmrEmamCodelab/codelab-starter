using System.Net;
using System.Text.Json;
using Codelab.Security.Problems;
using Codelab.Security.RateLimiting;
using Codelab.Security.Tests.TestSupport;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Options;

namespace Codelab.Security.Tests.Edge;

[Collection(SharedCodecGroup.Name)]
public sealed class ClientRateLimitingTests
{
    private const string Proxy = "192.0.2.5";

    [Fact]
    public void Without_a_trusted_proxy_the_forwarded_header_is_ignored()
    {
        Assert.Equal("203.0.113.9", Resolve([], remote: "203.0.113.9", forwardedFor: "198.51.100.1"));
    }

    [Fact]
    public void A_header_from_an_untrusted_connection_is_ignored_even_when_proxies_are_listed()
    {
        Assert.Equal("203.0.113.9", Resolve([Proxy], remote: "203.0.113.9", forwardedFor: "198.51.100.1"));
    }

    [Fact]
    public void Behind_a_trusted_proxy_the_rightmost_untrusted_hop_is_the_client()
    {
        Assert.Equal("198.51.100.7", Resolve([Proxy], remote: Proxy, forwardedFor: "198.51.100.200, 198.51.100.7"));
    }

    [Fact]
    public void A_chain_of_trusted_proxies_is_walked_past_cidr_ranges_included()
    {
        Assert.Equal("198.51.100.7", Resolve([Proxy, "203.0.113.0/24"], remote: Proxy, forwardedFor: "198.51.100.7, 203.0.113.44"));
    }

    [Fact]
    public void A_malformed_hop_stops_the_walk_at_the_last_trusted_address()
    {
        Assert.Equal(Proxy, Resolve([Proxy], remote: Proxy, forwardedFor: "198.51.100.7, not-an-ip"));
    }

    [Fact]
    public void An_ipv4_mapped_connection_matches_an_ipv4_proxy_entry()
    {
        Assert.Equal("198.51.100.7", Resolve([Proxy], remote: "::ffff:192.0.2.5", forwardedFor: "198.51.100.7"));
    }

    [Fact]
    public void A_trusted_proxy_entry_that_is_neither_address_nor_range_is_refused()
    {
        var result = new ClientRateLimitOptionsValidator().Validate(name: null, new ClientRateLimitOptions { TrustedProxies = ["proxy.example"] });

        Assert.True(result.Failed);
    }

    [Fact]
    public async Task A_client_over_its_window_is_refused_429_with_the_code_while_other_clients_pass()
    {
        var options = SecurityTestHost.BaseOptions();
        options.Settings[$"{SecuritySections.RateLimiting}:PermitLimit"] = "2";
        options.Settings[$"{SecuritySections.RateLimiting}:TrustedProxies:0"] = Proxy;
        options.MapEndpoints = endpoints => endpoints.MapGet("/limited", () => "ok").RequireRateLimiting(ClientRateLimitOptions.PolicyName);
        await using var host = await SecurityTestHost.StartAsync(options);

        Assert.Equal(HttpStatusCode.OK, (await SendAsync(host, "198.51.100.1")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await SendAsync(host, "198.51.100.1")).StatusCode);
        var refused = await SendAsync(host, "198.51.100.1");
        Assert.Equal(HttpStatusCode.OK, (await SendAsync(host, "198.51.100.2")).StatusCode);

        Assert.Equal(HttpStatusCode.TooManyRequests, refused.StatusCode);
        using var problem = JsonDocument.Parse(await refused.Content.ReadAsStringAsync());
        Assert.Equal(SecurityProblemCodes.RateLimited, problem.RootElement.GetProperty("code").GetString());
    }

    private static Task<HttpResponseMessage> SendAsync(SecurityTestHost host, string forwardedFor)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "/limited");
        request.Headers.Add(TestConnection.RemoteAddressHeader, Proxy);
        request.Headers.Add(ClientRateLimitOptions.ForwardedForHeader, forwardedFor);
        return host.Client.SendAsync(request);
    }

    private static string? Resolve(string[] proxies, string remote, string forwardedFor)
    {
        var monitor = new StaticMonitor<ClientRateLimitOptions>(new ClientRateLimitOptions { TrustedProxies = proxies });
        var context = new DefaultHttpContext();
        context.Connection.RemoteIpAddress = IPAddress.Parse(remote);
        context.Request.Headers[ClientRateLimitOptions.ForwardedForHeader] = forwardedFor;
        return new ClientAddressResolver(monitor).Resolve(context)?.ToString();
    }
}

/// <summary>An options monitor over a fixed value.</summary>
internal sealed class StaticMonitor<T>(T value) : IOptionsMonitor<T>
{
    public T CurrentValue => value;

    public T Get(string? name) => value;

    public IDisposable? OnChange(Action<T, string?> listener) => null;
}
