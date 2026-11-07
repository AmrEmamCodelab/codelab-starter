using Codelab.Gateway.Api.Tests.TestSupport;
using Codelab.Integration.Gateway;
using Codelab.Security.ServiceAuth;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Codelab.Gateway.Api.Tests.Http;

/// <summary>
/// The backend's real relay client (Codelab.Integration's <see cref="GatewayRelayClient"/>, registered with
/// AddCodelabGatewayClient) against the real gateway: both sides of the contract agree on routes, media types, JSON,
/// the assertion's audience and binding, and the failure codes.
/// </summary>
public sealed class RelayClientContractTests : IClassFixture<GatewayFixture>
{
    private readonly GatewayFixture _fixture;

    public RelayClientContractTests(GatewayFixture fixture)
    {
        ArgumentNullException.ThrowIfNull(fixture);
        _fixture = fixture;
        fixture.Upstream.SmsStatus = 200;
        fixture.Upstream.SmsBody = FakeUpstream.SmsJson;
        fixture.Upstream.TokenStatus = 200;
    }

    [Fact]
    public async Task The_client_relays_an_sms()
    {
        await using var services = ClientServices();
        var client = services.GetRequiredService<GatewayRelayClient>();

        var result = await client.RelaySmsAsync(new SmsRelayRequest { To = "+05XXXXXX20", Text = "Your code is 1234", Language = GatewayLanguages.English });

        Assert.True(result.Accepted);
        Assert.Equal("msg-1", result.ProviderMessageId);
        Assert.False(string.IsNullOrWhiteSpace(result.ReferenceCode));
    }

    [Fact]
    public async Task The_client_relays_a_token_request_and_reads_the_jwks()
    {
        await using var services = ClientServices();
        var client = services.GetRequiredService<GatewayRelayClient>();
        var form = new SsoTokenRelayRequest([new("grant_type", "authorization_code"), new("code", "c0de")]);

        using var token = await client.RelaySsoTokenAsync(form.ToContent());
        using var jwks = await client.GetSsoJwksAsync();

        Assert.Equal("access-1", token.RootElement.GetProperty("access_token").GetString());
        Assert.Equal(1, jwks.RootElement.GetProperty("keys").GetArrayLength());
    }

    [Fact]
    public async Task A_provider_outage_reaches_the_client_as_its_failure_code()
    {
        _fixture.Upstream.SmsStatus = 503;
        await using var services = ClientServices();
        var client = services.GetRequiredService<GatewayRelayClient>();

        var failure = await Assert.ThrowsAsync<GatewayRelayException>(() =>
            client.RelaySmsAsync(new SmsRelayRequest { To = "+05XXXXXX21", Text = "hello", Language = GatewayLanguages.Arabic }));

        Assert.Equal(GatewayFailureCodes.UpstreamUnavailable, failure.Code);
    }

    private ServiceProvider ClientServices()
    {
        var identity = _fixture.Host.CallerIdentity;
        var settings = new Dictionary<string, string?>
        {
            [$"{GatewayOptions.SectionName}:BaseUrl"] = new Uri(_fixture.Host.Server.BaseAddress, "/").ToString().Replace("http://", "https://", StringComparison.Ordinal),
            [$"{ServiceAssertionClientOptions.SectionName}:ServiceName"] = identity.ServiceName,
            [$"{ServiceAssertionClientOptions.SectionName}:KeyId"] = identity.KeyId,
            [$"{ServiceAssertionClientOptions.SectionName}:PrivateKeyPem"] = identity.PrivateKeyPem,
            [$"{GatewayOptions.SectionName}:Retry:MaxRetries"] = "0",
        };
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(settings).Build();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(_fixture.Host.Protocol);
        services.AddCodelabGatewayClient(configuration);
        services.AddHttpClient(GatewayRelayClient.Name).ConfigurePrimaryHttpMessageHandler(() => _fixture.Host.Server.CreateHandler());
        return services.BuildServiceProvider(validateScopes: true);
    }
}
