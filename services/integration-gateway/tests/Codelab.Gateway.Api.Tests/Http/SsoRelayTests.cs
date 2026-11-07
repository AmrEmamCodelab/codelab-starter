using System.Net;
using System.Text;
using Codelab.Errors;
using Codelab.Gateway.Api.Tests.TestSupport;
using Codelab.Integration.Gateway;

namespace Codelab.Gateway.Api.Tests.Http;

/// <summary>
/// The SSO profile relays the OIDC back channel: the token request form byte for byte to the provider and its JSON
/// back verbatim (never cacheable), the JWKS document from a cache; every upstream failure is a coded refusal.
/// </summary>
public sealed class SsoRelayTests : IClassFixture<GatewayFixture>
{
    private const string Form = "grant_type=authorization_code&code=c0de&redirect_uri=https%3A%2F%2Fapi.example.test%2Fsignin&code_verifier=v3r1f1er&client_id=starter-web&client_secret=s3cr3t";

    private readonly GatewayFixture _fixture;

    public SsoRelayTests(GatewayFixture fixture)
    {
        ArgumentNullException.ThrowIfNull(fixture);
        _fixture = fixture;
        fixture.Upstream.TokenStatus = 200;
        fixture.Upstream.TokenBody = FakeUpstream.TokenJson;
    }

    [Fact]
    public async Task The_token_request_reaches_the_provider_unchanged_and_its_answer_comes_back_verbatim()
    {
        using var response = await _fixture.Client.SendAsync(_fixture.Host.TokenRequest(Form));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(GatewayMediaTypes.Json, response.Content.Headers.ContentType?.MediaType);
        Assert.Equal(FakeUpstream.TokenJson, await response.Content.ReadAsStringAsync());
        Assert.True(response.Headers.CacheControl?.NoStore);
        Assert.Contains(Form, _fixture.Upstream.TokenForms);
    }

    [Fact]
    public async Task A_provider_refusal_is_502_rejected_without_the_providers_text()
    {
        _fixture.Upstream.TokenStatus = 400;
        _fixture.Upstream.TokenBody = """{"error":"invalid_grant","error_description":"upstream-detail"}""";

        using var response = await _fixture.Client.SendAsync(_fixture.Host.TokenRequest(Form));

        await Problems.AssertAsync(response, HttpStatusCode.BadGateway, GatewayFailureCodes.UpstreamRejected);
        Assert.DoesNotContain("upstream-detail", await response.Content.ReadAsStringAsync(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_provider_outage_is_503_unavailable()
    {
        _fixture.Upstream.TokenStatus = 503;

        using var response = await _fixture.Client.SendAsync(_fixture.Host.TokenRequest(Form));

        await Problems.AssertAsync(response, HttpStatusCode.ServiceUnavailable, GatewayFailureCodes.UpstreamUnavailable);
    }

    [Fact]
    public async Task A_provider_answer_that_is_not_json_is_502_rejected()
    {
        _fixture.Upstream.TokenBody = "<html>maintenance</html>";

        using var response = await _fixture.Client.SendAsync(_fixture.Host.TokenRequest(Form));

        await Problems.AssertAsync(response, HttpStatusCode.BadGateway, GatewayFailureCodes.UpstreamRejected);
    }

    [Fact]
    public async Task A_token_request_that_is_not_a_form_is_refused_before_the_provider()
    {
        var sent = _fixture.Upstream.TokenForms.Count;
        using var request = _fixture.Host.SignedRequest(HttpMethod.Post, GatewayRoutes.SsoToken, new StringContent("{}", Encoding.UTF8, GatewayMediaTypes.Json));

        using var response = await _fixture.Client.SendAsync(request);

        await Problems.AssertAsync(response, HttpStatusCode.BadRequest, ErrorCodes.Malformed);
        Assert.Equal(sent, _fixture.Upstream.TokenForms.Count);
    }

    [Fact]
    public async Task A_token_form_with_a_nameless_field_is_refused()
    {
        using var response = await _fixture.Client.SendAsync(_fixture.Host.TokenRequest("grant_type=authorization_code&=orphan"));

        await Problems.AssertAsync(response, HttpStatusCode.BadRequest, ErrorCodes.Malformed);
    }

    [Fact]
    public async Task The_jwks_document_is_relayed_and_then_served_from_the_cache()
    {
        var hits = _fixture.Upstream.JwksHits;

        using var first = await _fixture.Client.SendAsync(_fixture.Host.SignedRequest(HttpMethod.Get, GatewayRoutes.SsoJwks));
        using var second = await _fixture.Client.SendAsync(_fixture.Host.SignedRequest(HttpMethod.Get, GatewayRoutes.SsoJwks));

        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        Assert.Equal(FakeUpstream.JwksJson, await first.Content.ReadAsStringAsync());
        Assert.Equal(FakeUpstream.JwksJson, await second.Content.ReadAsStringAsync());
        Assert.InRange(_fixture.Upstream.JwksHits - hits, 0, 1);
    }
}

/// <summary>JWKS answers that must not be relayed, each on its own gateway (the cache would hide them otherwise).</summary>
public sealed class SsoJwksFailureTests
{
    [Fact]
    public async Task A_document_without_keys_is_502_rejected()
    {
        await using var upstream = await FakeUpstream.StartAsync();
        upstream.JwksBody = """{"issuer":"x"}""";
        await using var host = new GatewayHost(upstream);
        using var client = host.CreateClient();

        using var response = await client.SendAsync(host.SignedRequest(HttpMethod.Get, GatewayRoutes.SsoJwks));

        await Problems.AssertAsync(response, HttpStatusCode.BadGateway, GatewayFailureCodes.UpstreamRejected);
    }

    [Fact]
    public async Task A_redirect_is_never_followed()
    {
        await using var upstream = await FakeUpstream.StartAsync();
        await using var host = new GatewayHost(upstream, settings => settings["Gateway:Sso:JwksUri"] = upstream.Url(FakeUpstream.RedirectPath).ToString());
        using var client = host.CreateClient();

        using var response = await client.SendAsync(host.SignedRequest(HttpMethod.Get, GatewayRoutes.SsoJwks));

        await Problems.AssertAsync(response, HttpStatusCode.BadGateway, GatewayFailureCodes.UpstreamRejected);
        Assert.Equal(0, upstream.JwksHits);
    }

    [Fact]
    public async Task An_unreachable_provider_is_503_unavailable()
    {
        var upstream = await FakeUpstream.StartAsync();
        await using var host = new GatewayHost(upstream);
        using var client = host.CreateClient();
        await upstream.DisposeAsync();

        using var response = await client.SendAsync(host.SignedRequest(HttpMethod.Get, GatewayRoutes.SsoJwks));

        await Problems.AssertAsync(response, HttpStatusCode.ServiceUnavailable, GatewayFailureCodes.UpstreamUnavailable);
    }
}
