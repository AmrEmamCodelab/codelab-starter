using System.Net;
using System.Text;
using Codelab.Identity.Oidc;
using Codelab.Integration.Gateway;
using Codelab.Security.Tickets;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Starter.Application.SingleSignOn;
using Starter.Modules.Identity.Infrastructure.LoginModes;

namespace Starter.Modules.Identity.Tests.LoginModes;

/// <summary>A fake gateway behind the relay client: records each request and answers from a script.</summary>
internal sealed class FakeGatewayHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
{
    public List<(HttpMethod Method, string Path, string? MediaType, string Body)> Received { get; } = [];

    public static FakeGatewayHandler Answer(HttpStatusCode status, string json, string mediaType = "application/json") =>
        new(_ => new HttpResponseMessage(status) { Content = new StringContent(json, Encoding.UTF8, mediaType) });

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var body = request.Content is null ? string.Empty : await request.Content.ReadAsStringAsync(cancellationToken);
        Received.Add((request.Method, request.RequestUri!.AbsolutePath, request.Content?.Headers.ContentType?.MediaType, body));
        return respond(request);
    }
}

public sealed class GatewaySsoBackChannelTests
{
    private static readonly Uri GatewayBase = new("https://gateway.example.test/dmz/");

    [Fact]
    public async Task The_token_request_is_relayed_as_a_form_and_the_provider_json_comes_back()
    {
        var gateway = FakeGatewayHandler.Answer(HttpStatusCode.OK, "{\"id_token\":\"a.b.c\",\"token_type\":\"Bearer\"}");

        var response = await Channel(gateway).RedeemCodeAsync(
            [new("grant_type", "authorization_code"), new("code", "c 1"), new("code_verifier", "v1")],
            CancellationToken.None);

        Assert.Equal(OidcBackChannelOutcome.Answered, response.Outcome);
        Assert.Equal((int)HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("{\"id_token\":\"a.b.c\",\"token_type\":\"Bearer\"}", response.Body);
        var request = Assert.Single(gateway.Received);
        Assert.Equal(HttpMethod.Post, request.Method);
        Assert.Equal("/dmz" + GatewayRoutes.SsoToken, request.Path);
        Assert.Equal("application/x-www-form-urlencoded", request.MediaType);
        Assert.Equal("grant_type=authorization_code&code=c+1&code_verifier=v1", request.Body);
    }

    [Fact]
    public async Task The_jwks_is_fetched_through_the_relay()
    {
        var gateway = FakeGatewayHandler.Answer(HttpStatusCode.OK, "{\"keys\":[]}");

        var response = await Channel(gateway).GetJwksAsync(CancellationToken.None);

        Assert.Equal("{\"keys\":[]}", response.Body);
        var request = Assert.Single(gateway.Received);
        Assert.Equal(HttpMethod.Get, request.Method);
        Assert.Equal("/dmz" + GatewayRoutes.SsoJwks, request.Path);
    }

    [Fact]
    public async Task A_provider_refusal_relayed_as_upstream_rejected_is_a_rejection()
    {
        var gateway = FakeGatewayHandler.Answer(HttpStatusCode.BadGateway, Problem(GatewayFailureCodes.UpstreamRejected), "application/problem+json");

        var response = await Channel(gateway).RedeemCodeAsync([new("code", "c1")], CancellationToken.None);

        Assert.Equal(OidcBackChannelOutcome.Rejected, response.Outcome);
        Assert.Equal((int)HttpStatusCode.BadGateway, response.StatusCode);
        Assert.Equal(GatewayFailureCodes.UpstreamRejected, response.FailureReason);
        Assert.Null(response.Body);
    }

    [Theory]
    [InlineData(HttpStatusCode.ServiceUnavailable, GatewayFailureCodes.UpstreamUnavailable)]
    [InlineData(HttpStatusCode.NotFound, GatewayFailureCodes.ProfileDisabled)]
    public async Task An_unavailable_upstream_or_a_disabled_profile_is_unavailable(HttpStatusCode status, string code)
    {
        var gateway = FakeGatewayHandler.Answer(status, Problem(code), "application/problem+json");

        var response = await Channel(gateway).RedeemCodeAsync([new("code", "c1")], CancellationToken.None);

        Assert.Equal(OidcBackChannelOutcome.Unavailable, response.Outcome);
        Assert.Equal(code, response.FailureReason);
    }

    [Fact]
    public async Task The_gateway_refusing_this_api_is_a_deployment_fault_not_a_user_rejection()
    {
        var gateway = new FakeGatewayHandler(_ => new HttpResponseMessage(HttpStatusCode.Unauthorized));

        var response = await Channel(gateway).GetJwksAsync(CancellationToken.None);

        Assert.Equal(OidcBackChannelOutcome.Unavailable, response.Outcome);
    }

    [Fact]
    public async Task An_unreachable_gateway_is_unavailable()
    {
        var gateway = new FakeGatewayHandler(_ => throw new HttpRequestException(HttpRequestError.ConnectionError, "refused"));

        var response = await Channel(gateway).RedeemCodeAsync([new("code", "c1")], CancellationToken.None);

        Assert.Equal(OidcBackChannelOutcome.Unavailable, response.Outcome);
        Assert.Equal(GatewayFailureCodes.UpstreamUnavailable, response.FailureReason);
    }

    [Fact]
    public void The_channel_reports_the_gateway_relay_mode() =>
        Assert.Equal(OidcBackChannel.GatewayRelay, Channel(FakeGatewayHandler.Answer(HttpStatusCode.OK, "{}")).Mode);

    private static GatewaySsoBackChannel Channel(HttpMessageHandler gateway)
    {
        var services = new ServiceCollection();
        services.AddTransient(_ => new GatewayRelayClient(new HttpClient(gateway, disposeHandler: false) { BaseAddress = GatewayBase }));
        return new GatewaySsoBackChannel(services.BuildServiceProvider());
    }

    private static string Problem(string code) => $"{{\"status\":502,\"code\":\"{code}\",\"referenceCode\":\"GW-1\"}}";
}

/// <summary>How <c>AddIdentityLoginModes</c> picks the SSO back channel from configuration.</summary>
public sealed class SsoBackChannelRegistrationTests
{
    private const string Sso = OidcOptions.SectionName;
    private const string GatewayBaseUrl = "https://gateway.example.test/";

    [Fact]
    public void The_gateway_relay_setting_registers_the_relay_back_channel_and_the_gateway_client()
    {
        var services = Services(new() { [$"{Sso}:{nameof(OidcOptions.BackChannel)}"] = "2" });

        using var provider = services.BuildServiceProvider();

        Assert.IsType<GatewaySsoBackChannel>(provider.GetRequiredService<IOidcBackChannel>());
        Assert.Single(services, descriptor => descriptor.ServiceType == typeof(GatewayRelayClient));
        Assert.Equal(OidcBackChannel.GatewayRelay, provider.GetRequiredService<IOptions<OidcOptions>>().Value.BackChannel);
    }

    [Fact]
    public void The_named_value_is_read_too()
    {
        using var provider = Services(new() { [$"{Sso}:{nameof(OidcOptions.BackChannel)}"] = nameof(OidcBackChannel.GatewayRelay) }).BuildServiceProvider();

        Assert.IsType<GatewaySsoBackChannel>(provider.GetRequiredService<IOidcBackChannel>());
    }

    [Fact]
    public void Without_the_setting_the_direct_back_channel_stays_and_no_gateway_client_is_registered()
    {
        var services = Services([]);

        using var provider = services.BuildServiceProvider();

        Assert.Equal(OidcBackChannel.Direct, provider.GetRequiredService<IOidcBackChannel>().Mode);
        Assert.DoesNotContain(services, descriptor => descriptor.ServiceType == typeof(GatewayRelayClient));
    }

    [Fact]
    public void An_unreadable_value_registers_no_relay_and_fails_the_start()
    {
        using var provider = Services(new() { [$"{Sso}:{nameof(OidcOptions.BackChannel)}"] = "sideways" }).BuildServiceProvider();

        Assert.Equal(OidcBackChannel.Direct, provider.GetRequiredService<IOidcBackChannel>().Mode);
        Assert.ThrowsAny<InvalidOperationException>(() => provider.GetRequiredService<IOptions<OidcOptions>>().Value);
    }

    [Theory]
    [InlineData("2", OidcBackChannel.GatewayRelay)]
    [InlineData(null, OidcBackChannel.Direct)]
    public void With_sso_the_key_set_reads_through_the_configured_back_channel(string? backChannel, OidcBackChannel expected)
    {
        using var provider = Services(new() { [$"{Sso}:{nameof(OidcOptions.BackChannel)}"] = backChannel }).BuildServiceProvider();

        Assert.IsType<SsoIdentityProviderKeySet>(provider.GetRequiredService<IIdentityProviderKeySet>());
        Assert.Equal(expected, provider.GetRequiredService<IOidcBackChannel>().Mode);
    }

    [Fact]
    public void Without_sso_no_key_set_is_offered()
    {
        using var provider = Services(new() { ["Auth:Modes:0"] = "basic" }).BuildServiceProvider();

        Assert.Null(provider.GetService<IIdentityProviderKeySet>());
    }

    [Fact]
    public void The_gateway_client_is_registered_once_however_many_modules_ask()
    {
        var services = new ServiceCollection();
        var configuration = new ConfigurationBuilder().Build();

        services.AddGatewayClientOnce(configuration);
        services.AddGatewayClientOnce(configuration);

        Assert.Single(services, descriptor => descriptor.ServiceType == typeof(GatewayRelayClient));
    }

    private static ServiceCollection Services(Dictionary<string, string?> extra)
    {
        var settings = new Dictionary<string, string?>
        {
            ["Auth:Modes:0"] = "sso",
            [$"{Sso}:Authority"] = "https://idp.example.org",
            [$"{Sso}:ClientId"] = "starter-web",
            [$"{Sso}:RedirectUri"] = "https://app.example.org/api/auth/sso/signin-oidc",
            [$"{Sso}:AuthorizationEndpoint"] = "https://idp.example.org/authorize",
            [$"{Sso}:TokenEndpoint"] = "https://idp.example.org/token",
            [$"{Sso}:EndSessionEndpoint"] = "https://idp.example.org/end-session",
            [$"{Sso}:JwksUri"] = "https://idp.example.org/jwks",
            [$"{GatewayOptions.SectionName}:BaseUrl"] = GatewayBaseUrl,
        };
        foreach (var (key, value) in extra)
        {
            settings[key] = value;
        }

        var services = new ServiceCollection();
        services.AddSingleton<IHostEnvironment>(new TestHostEnvironment(Environments.Production)).AddLogging();
        services.AddOptions<SealedTicketOptions>();
        services.AddIdentityLoginModes(new ConfigurationBuilder().AddInMemoryCollection(settings).Build());
        return services;
    }
}
