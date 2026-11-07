using System.Net;
using System.Net.Http.Headers;
using System.Text;
using Codelab.Errors;
using Codelab.Gateway.Api.Tests.TestSupport;
using Codelab.Integration.Gateway;
using Codelab.Security.ServiceAuth;

namespace Codelab.Gateway.Api.Tests.Http;

/// <summary>
/// The gateway's one credential is the ES256 service assertion of a trusted caller, bound to the method, the path and
/// the body, and usable once. Anything else (nothing, a JWT, an API key, a replayed, rebound, foreign or wrongly
/// addressed assertion) is 401, and nothing reaches the upstream. Health stays anonymous.
/// </summary>
public sealed class InboundAuthenticationTests : IClassFixture<GatewayFixture>
{
    private const string ValidSms = """{"to":"+05XXXXXX01","text":"Your code is 1234","language":2}""";

    private readonly GatewayFixture _fixture;

    public InboundAuthenticationTests(GatewayFixture fixture)
    {
        ArgumentNullException.ThrowIfNull(fixture);
        _fixture = fixture;
        fixture.Upstream.SmsStatus = 200;
        fixture.Upstream.SmsBody = FakeUpstream.SmsJson;
    }

    [Fact]
    public async Task A_signed_request_from_the_trusted_caller_is_relayed()
    {
        using var response = await _fixture.Client.SendAsync(_fixture.Host.SmsRequest(ValidSms));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task No_credential_is_401()
    {
        using var response = await _fixture.Client.SendAsync(Unsigned());

        await AssertUnauthenticatedAsync(response);
    }

    [Fact]
    public async Task A_jwt_bearer_is_401()
    {
        using var request = Unsigned();
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", HttpCredentials.UnsignedJwt);

        using var response = await _fixture.Client.SendAsync(request);

        await AssertUnauthenticatedAsync(response);
    }

    [Fact]
    public async Task An_api_key_is_401()
    {
        using var request = Unsigned();
        request.Headers.Add(HttpCredentials.ApiKeyHeader, HttpCredentials.ApiKey);

        using var response = await _fixture.Client.SendAsync(request);

        await AssertUnauthenticatedAsync(response);
    }

    [Fact]
    public async Task A_replayed_assertion_is_401()
    {
        var assertion = _fixture.Host.Assertion(HttpMethod.Post, GatewayRoutes.Sms, Json(ValidSms));
        using var first = await _fixture.Client.SendAsync(WithAssertion(Unsigned(), assertion));
        using var replay = await _fixture.Client.SendAsync(WithAssertion(Unsigned(), assertion));

        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        await AssertUnauthenticatedAsync(replay);
    }

    [Fact]
    public async Task An_assertion_bound_to_another_body_is_401()
    {
        var assertion = _fixture.Host.Assertion(HttpMethod.Post, GatewayRoutes.Sms, Json(ValidSms.Replace("1234", "9999", StringComparison.Ordinal)));

        using var response = await _fixture.Client.SendAsync(WithAssertion(Unsigned(), assertion));

        await AssertUnauthenticatedAsync(response);
    }

    [Fact]
    public async Task An_assertion_bound_to_another_route_is_401()
    {
        var assertion = _fixture.Host.Assertion(HttpMethod.Post, GatewayRoutes.SsoToken, Json(ValidSms));

        using var response = await _fixture.Client.SendAsync(WithAssertion(Unsigned(), assertion));

        await AssertUnauthenticatedAsync(response);
    }

    [Fact]
    public async Task An_assertion_for_another_audience_is_401()
    {
        var identity = _fixture.Host.CallerIdentity;
        var foreign = new ServiceAssertionClientOptions { ServiceName = identity.ServiceName, KeyId = identity.KeyId, PrivateKeyPem = identity.PrivateKeyPem, Audience = "starter-attachments" };

        using var response = await _fixture.Client.SendAsync(WithAssertion(Unsigned(), Issue(foreign)));

        await AssertUnauthenticatedAsync(response);
    }

    [Fact]
    public async Task An_assertion_signed_by_an_untrusted_key_is_401()
    {
        var identity = _fixture.Host.CallerIdentity;
        var (privatePem, _) = TestKeys.NewServiceKeyPair();
        var impostor = new ServiceAssertionClientOptions { ServiceName = identity.ServiceName, KeyId = identity.KeyId, PrivateKeyPem = privatePem, Audience = identity.Audience };

        using var response = await _fixture.Client.SendAsync(WithAssertion(Unsigned(), Issue(impostor)));

        await AssertUnauthenticatedAsync(response);
    }

    [Theory]
    [InlineData("/health/live")]
    [InlineData("/health/ready")]
    public async Task Health_is_anonymous(string path)
    {
        using var response = await _fixture.Client.GetAsync(new Uri(path, UriKind.Relative));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    private static StringContent Json(string json) => new(json, Encoding.UTF8, GatewayMediaTypes.Json);

    private static HttpRequestMessage Unsigned() => new(HttpMethod.Post, GatewayRoutes.Sms) { Content = Json(ValidSms) };

    private HttpRequestMessage WithAssertion(HttpRequestMessage request, string assertion)
    {
        request.Headers.Add(_fixture.Host.Protocol.ServiceAssertionHeaderName, assertion);
        return request;
    }

    private string Issue(ServiceAssertionClientOptions identity)
    {
        using var issuer = new ServiceAssertionIssuer(identity, TimeProvider.System, _fixture.Host.Protocol);
        return issuer.Issue(HttpMethod.Post, new Uri(_fixture.Host.Server.BaseAddress, GatewayRoutes.Sms), Encoding.UTF8.GetBytes(ValidSms));
    }

    private static async Task AssertUnauthenticatedAsync(HttpResponseMessage response) =>
        await Problems.AssertAsync(response, HttpStatusCode.Unauthorized, ErrorCodes.Unauthenticated);
}
