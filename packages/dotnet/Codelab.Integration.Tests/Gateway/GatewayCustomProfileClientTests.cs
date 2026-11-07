using System.Net;
using Codelab.Integration.Gateway;
using Codelab.Integration.Tests.Gateway.Support;
using Microsoft.Extensions.DependencyInjection;

namespace Codelab.Integration.Tests.Gateway;

/// <summary>The generic relay methods a project's own profile client builds on, and the relay-route guard.</summary>
public sealed class GatewayCustomProfileClientTests
{
    private const string LookupRoute = "/relay/registry-lookup/records";
    private readonly ServiceKeyPair _keys = ServiceKeyPair.Create();

    [Fact]
    public async Task A_custom_profile_post_is_signed_sent_once_and_read()
    {
        var stub = new RecordingHandler(_ => new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("{\"found\":true,\"name\":\"n\"}") });
        using var provider = GatewayClientHost.Build(GatewayClientHost.Settings(_keys), stub);

        var answer = await provider.GetRequiredService<GatewayRelayClient>().PostJsonAsync<LookupRequest, LookupAnswer>(LookupRoute, new LookupRequest("r-1"));

        var sent = Assert.Single(stub.Received);
        Assert.Equal(new Uri("https://gateway.example.test/dmz/relay/registry-lookup/records"), sent.Uri);
        Assert.Equal("{\"recordId\":\"r-1\"}", sent.BodyText);
        Assert.NotNull(sent.Assertion);
        Assert.True(answer.Found);
    }

    [Fact]
    public async Task A_custom_profile_post_that_answers_null_is_unusable()
    {
        var stub = new RecordingHandler(_ => new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("null") });
        using var provider = GatewayClientHost.Build(GatewayClientHost.Settings(_keys), stub);

        var failure = await Assert.ThrowsAsync<GatewayRelayException>(() => provider.GetRequiredService<GatewayRelayClient>().PostJsonAsync<LookupRequest, LookupAnswer>(LookupRoute, new LookupRequest("r-1")));

        Assert.Equal(GatewayFailureCodes.UpstreamRejected, failure.Code);
    }

    [Fact]
    public async Task A_custom_profile_get_returns_the_json()
    {
        var stub = new RecordingHandler(_ => new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("{\"ok\":1}") });
        using var provider = GatewayClientHost.Build(GatewayClientHost.Settings(_keys), stub);

        using var document = await provider.GetRequiredService<GatewayRelayClient>().GetJsonAsync(LookupRoute);

        Assert.Equal(HttpMethod.Get, Assert.Single(stub.Received).Method);
        Assert.Equal(1, document.RootElement.GetProperty("ok").GetInt32());
    }

    [Theory]
    [InlineData("/relay/sms", "relay/sms")]
    [InlineData("/relay/sso/token", "relay/sso/token")]
    [InlineData("/relay/registry-lookup/a/b", "relay/registry-lookup/a/b")]
    public void A_relay_route_becomes_a_relative_target(string route, string target) =>
        Assert.Equal(target, GatewayRelayClient.RelayTarget(route));

    [Theory]
    [InlineData("relay/sms")]
    [InlineData("/relay")]
    [InlineData("/relay/")]
    [InlineData("/health/live")]
    [InlineData("/relay/SMS")]
    [InlineData("/relay/sms/")]
    [InlineData("/relay/sms/../../admin")]
    [InlineData("/relay/sms/./x")]
    [InlineData("/relay/sms?x=1")]
    [InlineData("/relay/sms/%2e%2e")]
    [InlineData("/relay/sms\\x")]
    [InlineData("https://elsewhere.example.test/relay/sms")]
    [InlineData(" ")]
    public void Anything_but_a_relay_route_is_refused(string route) =>
        Assert.Throws<ArgumentException>(() => GatewayRelayClient.RelayTarget(route));

    private sealed record LookupRequest(string RecordId);

    private sealed record LookupAnswer(bool Found, string? Name);
}
