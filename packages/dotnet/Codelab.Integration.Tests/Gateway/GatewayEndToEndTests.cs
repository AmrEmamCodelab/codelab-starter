using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using Codelab.Integration.Gateway;
using Codelab.Integration.Tests.Gateway.Support;
using Codelab.Security.Replay;
using Codelab.Security.ServiceAuth;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Codelab.Integration.Tests.Gateway;

/// <summary>A relay SMS profile for the tests: answers with the authenticated calling service as the message id.</summary>
internal sealed class SmsProfile : IGatewayProfile
{
    public string Key => GatewayProfileKey.Sms;

    public void Map(IEndpointRouteBuilder endpoints) =>
        endpoints.MapPost(GatewayRoutes.Sms, (SmsRelayRequest request, ClaimsPrincipal user) =>
            TypedResults.Ok(new SmsRelayResult { Accepted = SmsRelayRequestValidator.Validate(request).Count == 0, ProviderMessageId = user.FindFirstValue(ClaimTypes.NameIdentifier) }));
}

/// <summary>An SSO profile that is registered but not enabled in the tests.</summary>
internal sealed class SsoProfile : IGatewayProfile
{
    public string Key => GatewayProfileKey.Sso;

    public void Map(IEndpointRouteBuilder endpoints) => endpoints.MapGet(GatewayRoutes.SsoJwks, () => TypedResults.Ok());
}

/// <summary>
/// The real chain: the relay client (Development, plain HTTP on loopback) signs, a Kestrel gateway authenticates with
/// the Codelab service-assertion scheme, maps the enabled profile and answers 404 GATEWAY_PROFILE_DISABLED for the
/// disabled one.
/// </summary>
public sealed class GatewayEndToEndTests : IAsyncLifetime
{
    private readonly ServiceKeyPair _keys = ServiceKeyPair.Create();
    private WebApplication _gateway = null!;
    private Uri _address = null!;

    public async Task InitializeAsync()
    {
        var builder = WebApplication.CreateSlimBuilder();
        builder.Logging.ClearProviders();
        builder.WebHost.ConfigureKestrel(kestrel => kestrel.Listen(IPAddress.Loopback, port: 0));
        builder.Configuration.AddInMemoryCollection(GatewaySettings());
        builder.Services.AddSingleton(GatewayClientHost.Protocol);
        builder.Services.AddSingleton<IReplayGuard>(new MemoryReplayGuard(TimeProvider.System, entryLimit: 100));
        builder.Services.AddSingleton(TimeProvider.System);
        builder.Services.AddAuthentication().AddCodelabServiceAssertion(builder.Configuration);
        builder.Services.AddAuthorization();
        builder.Services.AddProblemDetails();
        builder.Services.AddCodelabGatewayProfiles(builder.Configuration).AddGatewayProfile<SmsProfile>().AddGatewayProfile<SsoProfile>();
        _gateway = builder.Build();
        _gateway.MapGroup(string.Empty)
            .RequireAuthorization(policy => policy.AddAuthenticationSchemes(ServiceAssertionAuthenticationHandler.SchemeName).RequireAuthenticatedUser())
            .MapGatewayProfiles();
        await _gateway.StartAsync();
        _address = new Uri(_gateway.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.Single());
    }

    public async Task DisposeAsync()
    {
        await _gateway.StopAsync();
        await _gateway.DisposeAsync();
    }

    [Fact]
    public async Task A_signed_sms_relay_is_authenticated_as_the_calling_service()
    {
        using var caller = Caller();

        var result = await caller.GetRequiredService<GatewayRelayClient>().RelaySmsAsync(new SmsRelayRequest { To = "05XXXXXXXX", Text = "x", Language = GatewayLanguages.Arabic });

        Assert.True(result.Accepted);
        Assert.Equal(GatewayClientHost.CallerName, result.ProviderMessageId);
    }

    [Fact]
    public async Task A_disabled_profile_answers_profile_disabled()
    {
        using var caller = Caller();

        var failure = await Assert.ThrowsAsync<GatewayRelayException>(() => caller.GetRequiredService<GatewayRelayClient>().GetSsoJwksAsync());

        Assert.Equal(GatewayFailureCodes.ProfileDisabled, failure.Code);
        Assert.Equal(HttpStatusCode.NotFound, failure.Failure.StatusCode);
    }

    [Fact]
    public async Task An_unsigned_call_is_refused_401()
    {
        using var http = new HttpClient { BaseAddress = _address };

        using var response = await http.PostAsJsonAsync(GatewayRoutes.Relative(GatewayRoutes.Sms), new SmsRelayRequest { To = "05XXXXXXXX", Text = "x", Language = GatewayLanguages.Arabic });

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    private ServiceProvider Caller() => GatewayClientHost.Build(GatewayClientHost.Settings(_keys, _address), environment: Environments.Development);

    private Dictionary<string, string?> GatewaySettings()
    {
        var settings = GatewayProfileRegistryTests.Settings(GatewayProfileKey.Sms);
        settings[$"{ServiceAssertionServerOptions.SectionName}:Audience"] = GatewayDefaults.Audience;
        settings[$"{ServiceAssertionServerOptions.SectionName}:TrustedServices:0:ServiceName"] = GatewayClientHost.CallerName;
        settings[$"{ServiceAssertionServerOptions.SectionName}:TrustedServices:0:KeyId"] = GatewayClientHost.CallerKeyId;
        settings[$"{ServiceAssertionServerOptions.SectionName}:TrustedServices:0:PublicKeyPem"] = _keys.PublicPem;
        return settings;
    }
}
