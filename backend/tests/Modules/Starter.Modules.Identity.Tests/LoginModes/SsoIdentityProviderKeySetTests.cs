using System.Net;
using Codelab.Identity.Oidc;
using Codelab.Integration.Gateway;
using Microsoft.Extensions.DependencyInjection;
using Starter.Modules.Identity.Infrastructure.LoginModes;

namespace Starter.Modules.Identity.Tests.LoginModes;

public sealed class SsoIdentityProviderKeySetTests
{
    private const string Jwks = "{\"keys\":[{\"kty\":\"RSA\",\"kid\":\"k1\"}]}";
    private static readonly Uri GatewayBase = new("https://gateway.example.test/dmz/");

    [Fact]
    public async Task Direct_mode_returns_the_providers_jwks()
    {
        var channel = new ScriptedBackChannel(OidcBackChannelResponse.Answered((int)HttpStatusCode.OK, Jwks));

        Assert.Equal(Jwks, await new SsoIdentityProviderKeySet(channel).ReadJwksAsync(CancellationToken.None));
        Assert.Equal(1, channel.JwksCalls);
    }

    [Fact]
    public async Task Relay_mode_reads_the_jwks_through_the_gateway()
    {
        var gateway = FakeGatewayHandler.Answer(HttpStatusCode.OK, Jwks);

        var jwks = await new SsoIdentityProviderKeySet(Relay(gateway)).ReadJwksAsync(CancellationToken.None);

        Assert.Equal(Jwks, jwks);
        var request = Assert.Single(gateway.Received);
        Assert.Equal((HttpMethod.Get, "/dmz" + GatewayRoutes.SsoJwks), (request.Method, request.Path));
    }

    [Fact]
    public async Task An_unavailable_relay_is_null()
    {
        var gateway = new FakeGatewayHandler(_ => throw new HttpRequestException(HttpRequestError.ConnectionError, "refused"));

        Assert.Null(await new SsoIdentityProviderKeySet(Relay(gateway)).ReadJwksAsync(CancellationToken.None));
    }

    [Fact]
    public async Task A_provider_error_status_or_an_unreachable_provider_is_null()
    {
        Assert.Null(await new SsoIdentityProviderKeySet(new ScriptedBackChannel(OidcBackChannelResponse.Answered((int)HttpStatusCode.InternalServerError, "oops")))
            .ReadJwksAsync(CancellationToken.None));
        Assert.Null(await new SsoIdentityProviderKeySet(new ScriptedBackChannel(OidcBackChannelResponse.Unavailable("HttpRequestException")))
            .ReadJwksAsync(CancellationToken.None));
    }

    private static GatewaySsoBackChannel Relay(HttpMessageHandler gateway)
    {
        var services = new ServiceCollection();
        services.AddTransient(_ => new GatewayRelayClient(new HttpClient(gateway, disposeHandler: false) { BaseAddress = GatewayBase }));
        return new GatewaySsoBackChannel(services.BuildServiceProvider());
    }

    private sealed class ScriptedBackChannel(OidcBackChannelResponse jwks) : IOidcBackChannel
    {
        public int JwksCalls { get; private set; }

        public OidcBackChannel Mode => OidcBackChannel.Direct;

        public Task<OidcBackChannelResponse> RedeemCodeAsync(IReadOnlyCollection<KeyValuePair<string, string>> form, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<OidcBackChannelResponse> GetJwksAsync(CancellationToken cancellationToken)
        {
            JwksCalls++;
            return Task.FromResult(jwks);
        }
    }
}
