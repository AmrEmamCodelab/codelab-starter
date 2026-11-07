using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Security.Cryptography;
using Codelab.Integration.Gateway;
using Codelab.Security;
using Codelab.Security.Replay;
using Codelab.Security.ServiceAuth;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Starter.Application.Notifications;
using Starter.Domain.Localization;
using Starter.Modules.Notifications.Application;
using Starter.Modules.Notifications.Application.Delivery;
using Starter.Modules.Notifications.Domain;
using Starter.Modules.Notifications.Infrastructure;
using Starter.Modules.Notifications.Infrastructure.Options;
using Starter.Modules.Notifications.Infrastructure.Sending;
using Starter.Modules.Notifications.Tests.Fakes;

namespace Starter.Modules.Notifications.Tests.Infrastructure;

/// <summary>
/// The SMS path end to end in process: the module registration (SMS enabled, gateway mode) builds the real relay
/// client; the outbox handler delivers a written notification through it to a stub gateway on loopback Kestrel that
/// authenticates with the real Codelab service-assertion scheme (ES256, audience, replay guard). Keys are generated at
/// run time. No database: the notification store is the module's fake.
/// </summary>
public sealed class GatewaySmsRelayIntegrationTests : IAsyncLifetime
{
    private const string CallerName = "starter-api";
    private const string CallerKeyId = "s2s-test";
    private static readonly SecurityProtocol Protocol = SecurityProtocol.Create(SecurityProtocol.TestPrefix);

    private readonly ECDsa _key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
    private readonly ConcurrentQueue<(string? Service, SmsRelayRequest Body)> _relayed = new();
    private WebApplication _gateway = null!;
    private Uri _address = null!;

    public async Task InitializeAsync()
    {
        var builder = WebApplication.CreateSlimBuilder();
        builder.Logging.ClearProviders();
        builder.WebHost.ConfigureKestrel(kestrel => kestrel.Listen(IPAddress.Loopback, port: 0));
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            [$"{ServiceAssertionServerOptions.SectionName}:Audience"] = GatewayDefaults.Audience,
            [$"{ServiceAssertionServerOptions.SectionName}:TrustedServices:0:ServiceName"] = CallerName,
            [$"{ServiceAssertionServerOptions.SectionName}:TrustedServices:0:KeyId"] = CallerKeyId,
            [$"{ServiceAssertionServerOptions.SectionName}:TrustedServices:0:PublicKeyPem"] = _key.ExportSubjectPublicKeyInfoPem(),
        });
        builder.Services.AddSingleton(Protocol);
        builder.Services.AddSingleton<IReplayGuard>(new MemoryReplayGuard(TimeProvider.System, entryLimit: 100));
        builder.Services.AddSingleton(TimeProvider.System);
        builder.Services.AddAuthentication().AddCodelabServiceAssertion(builder.Configuration);
        builder.Services.AddAuthorization();
        _gateway = builder.Build();
        _gateway.MapPost(GatewayRoutes.Sms, (SmsRelayRequest request, ClaimsPrincipal caller) =>
            {
                _relayed.Enqueue((caller.FindFirstValue(ClaimTypes.NameIdentifier), request));
                return TypedResults.Ok(new SmsRelayResult { Accepted = true, ProviderMessageId = "pm-1", ReferenceCode = "GW-1" });
            })
            .RequireAuthorization(policy => policy.AddAuthenticationSchemes(ServiceAssertionAuthenticationHandler.SchemeName).RequireAuthenticatedUser());
        await _gateway.StartAsync();
        _address = new Uri(_gateway.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.Single());
    }

    public async Task DisposeAsync()
    {
        await _gateway.StopAsync();
        await _gateway.DisposeAsync();
        _key.Dispose();
    }

    [Fact]
    public async Task An_sms_notification_is_relayed_through_the_gateway_with_a_service_assertion()
    {
        var harness = new NotificationsHarness();
        harness.Recipients.Users[NotificationsHarness.UserId] = harness.Recipients.Users[NotificationsHarness.UserId] with { Language = AppLanguage.Arabic };
        await harness.Writer().WriteAsync(NotificationsHarness.Test(NotificationChannels.Sms), CancellationToken.None);
        await using var api = ApiServices();
        await using var scope = api.CreateAsyncScope();
        var sender = scope.ServiceProvider.GetRequiredService<ISmsSender>();
        var handler = new NotificationSmsHandler(harness.Deliveries(), new SmsChannel(sender));

        await handler.HandleAsync(NotificationsHarness.DeliveryOf(Assert.Single(harness.Outbox.Messages)), CancellationToken.None);

        Assert.IsType<GatewaySmsSender>(sender);
        var (service, body) = Assert.Single(_relayed);
        Assert.Equal(CallerName, service);
        Assert.Equal(NotificationsHarness.UserMobile, body.To);
        Assert.Equal(GatewayLanguages.Arabic, body.Language);
        Assert.StartsWith("رسالة نصية تجريبية", body.Text, StringComparison.Ordinal);
        Assert.Equal(ChannelDeliveryState.Sent, Assert.Single(harness.Store.Saved).SmsState);
    }

    [Fact]
    public async Task The_stub_gateway_refuses_a_relay_without_an_assertion()
    {
        using var http = new HttpClient { BaseAddress = _address };

        using var response = await http.PostAsJsonAsync(GatewayRoutes.Relative(GatewayRoutes.Sms), new SmsRelayRequest { To = "x", Text = "x", Language = GatewayLanguages.Arabic });

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Empty(_relayed);
    }

    [Fact]
    public async Task A_gateway_that_does_not_trust_this_api_fails_the_delivery_for_good_and_leaves_it_pending()
    {
        var harness = new NotificationsHarness();
        await harness.Writer().WriteAsync(NotificationsHarness.Test(NotificationChannels.Sms), CancellationToken.None);
        using var stranger = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        await using var api = ApiServices(stranger.ExportPkcs8PrivateKeyPem());
        await using var scope = api.CreateAsyncScope();
        var handler = new NotificationSmsHandler(harness.Deliveries(), new SmsChannel(scope.ServiceProvider.GetRequiredService<ISmsSender>()));

        var failure = await Assert.ThrowsAsync<SmsDeliveryException>(() =>
            handler.HandleAsync(NotificationsHarness.DeliveryOf(Assert.Single(harness.Outbox.Messages)), CancellationToken.None));

        Assert.False(failure.IsRetryable);
        Assert.Empty(_relayed);
        Assert.Equal(ChannelDeliveryState.Pending, Assert.Single(harness.Store.Saved).SmsState);
    }

    /// <summary>The API side as the module registers it: SMS enabled in gateway mode, the S2S client identity, Development (plain http on loopback).</summary>
    private ServiceProvider ApiServices(string? privateKeyPem = null)
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            [$"{SmsDeliveryOptions.SectionName}:{nameof(SmsDeliveryOptions.Enabled)}"] = "true",
            [$"{SmsDeliveryOptions.SectionName}:{nameof(SmsDeliveryOptions.Mode)}"] = "2",
            [$"{GatewayOptions.SectionName}:{nameof(GatewayOptions.BaseUrl)}"] = _address.ToString(),
            [$"{ServiceAssertionClientOptions.SectionName}:ServiceName"] = CallerName,
            [$"{ServiceAssertionClientOptions.SectionName}:KeyId"] = CallerKeyId,
            [$"{ServiceAssertionClientOptions.SectionName}:PrivateKeyPem"] = privateKeyPem ?? _key.ExportPkcs8PrivateKeyPem(),
        }).Build();
        var services = new ServiceCollection();
        services.AddSingleton<IHostEnvironment>(new NamedEnvironment(Environments.Development));
        services.AddSingleton(Protocol);
        services.AddLogging();
        services.AddNotificationsModule(configuration);
        return services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
    }
}
