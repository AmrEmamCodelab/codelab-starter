using Codelab.Integration.Gateway;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Starter.Application.Authorization;
using Starter.Application.Messaging;
using Starter.Application.Notifications;
using Starter.Modules.Notifications.Api;
using Starter.Modules.Notifications.Application;
using Starter.Modules.Notifications.Application.Delivery;
using Starter.Modules.Notifications.Infrastructure;
using Starter.Modules.Notifications.Infrastructure.Options;
using Starter.Modules.Notifications.Infrastructure.Sending;
using Starter.Modules.Notifications.Tests.Infrastructure;

namespace Starter.Modules.Notifications.Tests;

public sealed class NotificationsModuleTests
{
    [Fact]
    public void The_module_is_named_after_its_assembly()
    {
        Assert.Equal("Notifications", new NotificationsModule().Name);
    }

    [Fact]
    public void Every_catalog_key_is_well_formed_unique_and_described()
    {
        var permissions = new NotificationsPermissionCatalogSource().Permissions;

        Assert.NotEmpty(permissions);
        Assert.All(permissions, permission => Assert.True(PermissionKeyFormat.IsValid(permission.Key), permission.Key));
        Assert.All(permissions, permission => Assert.False(string.IsNullOrWhiteSpace(permission.Description)));
        Assert.Equal(permissions.Count, permissions.Select(permission => permission.Key).Distinct(StringComparer.Ordinal).Count());
    }

    [Fact]
    public void Every_declared_key_is_in_the_catalog()
    {
        var declared = typeof(NotificationsPermissionKeys).GetFields()
            .Where(field => field.IsLiteral)
            .Select(field => (string)field.GetRawConstantValue()!);
        var catalog = new NotificationsPermissionCatalogSource().Permissions.Select(permission => permission.Key);

        Assert.Equal(declared.Order(StringComparer.Ordinal), catalog.Order(StringComparer.Ordinal));
    }

    [Fact]
    public void Registration_declares_the_catalog_the_service_and_one_handler_per_outbox_type()
    {
        var services = new ServiceCollection();

        services.AddNotificationsModule(new ConfigurationBuilder().Build());

        Assert.Contains(services, descriptor =>
            descriptor.ServiceType == typeof(IPermissionCatalogSource) && descriptor.ImplementationType == typeof(NotificationsPermissionCatalogSource));
        Assert.Contains(services, descriptor =>
            descriptor.ServiceType == typeof(INotificationService) && descriptor.ImplementationType == typeof(NotificationService));
        var handlers = services.Where(descriptor => descriptor.ServiceType == typeof(IOutboxHandler)).Select(descriptor => descriptor.ImplementationType).ToList();
        Assert.Equal([typeof(NotificationEmailHandler), typeof(NotificationSmsHandler)], handlers);
    }

    [Theory]
    [InlineData(null, typeof(SmtpEmailSender), typeof(FileSmsSender), false)]
    [InlineData("1", typeof(FileDropEmailSender), typeof(FileSmsSender), false)]
    [InlineData("2", typeof(SmtpEmailSender), typeof(FileSmsSender), false)]
    public void The_senders_follow_the_configured_modes(string? emailMode, Type emailSender, Type smsSender, bool smsEnabled)
    {
        var settings = new Dictionary<string, string?>
        {
            [$"{EmailDeliveryOptions.SectionName}:{nameof(EmailDeliveryOptions.Smtp)}:{nameof(SmtpRelayOptions.Host)}"] = "relay.internal.test",
        };
        if (emailMode is not null)
        {
            settings[$"{EmailDeliveryOptions.SectionName}:{nameof(EmailDeliveryOptions.Mode)}"] = emailMode;
        }

        var services = new ServiceCollection();
        services.AddSingleton<IHostEnvironment>(new NamedEnvironment(Environments.Development));
        services.AddNotificationsModule(new ConfigurationBuilder().AddInMemoryCollection(settings).Build());
        using var provider = services.BuildServiceProvider();

        Assert.IsType(emailSender, provider.GetRequiredService<IEmailSender>());
        Assert.IsType(smsSender, provider.GetRequiredService<ISmsSender>());
        Assert.Equal(smsEnabled, provider.GetRequiredService<DeliverySettings>().SmsEnabled);
        Assert.Equal(EmailDeliveryOptions.DefaultFromAddress, provider.GetRequiredService<IOptions<EmailDeliveryOptions>>().Value.FromAddress);
    }

    [Theory]
    [InlineData("true", "2", true)]
    [InlineData("false", "2", false)]
    [InlineData("true", "1", false)]
    public void The_gateway_sms_sender_and_client_are_registered_only_for_enabled_gateway_sms(string enabled, string mode, bool gateway)
    {
        var settings = new Dictionary<string, string?>
        {
            [$"{EmailDeliveryOptions.SectionName}:{nameof(EmailDeliveryOptions.Smtp)}:{nameof(SmtpRelayOptions.Host)}"] = "relay.internal.test",
            [$"{SmsDeliveryOptions.SectionName}:{nameof(SmsDeliveryOptions.Enabled)}"] = enabled,
            [$"{SmsDeliveryOptions.SectionName}:{nameof(SmsDeliveryOptions.Mode)}"] = mode,
            [$"{GatewayOptions.SectionName}:{nameof(GatewayOptions.BaseUrl)}"] = "https://gateway.example.test/",
        };
        var services = new ServiceCollection();
        services.AddSingleton<IHostEnvironment>(new NamedEnvironment(Environments.Development));

        services.AddNotificationsModule(new ConfigurationBuilder().AddInMemoryCollection(settings).Build());

        Assert.Equal(gateway ? 1 : 0, services.Count(descriptor => descriptor.ServiceType == typeof(GatewayRelayClient)));
        Assert.Equal(gateway, services.Any(descriptor => descriptor.ServiceType == typeof(GatewaySmsSender)));
        Assert.Equal(ServiceLifetime.Scoped, services.Single(descriptor => descriptor.ServiceType == typeof(ISmsSender)).Lifetime);
    }

    [Fact]
    public void A_gateway_client_registered_by_another_module_is_reused()
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            [$"{SmsDeliveryOptions.SectionName}:{nameof(SmsDeliveryOptions.Enabled)}"] = "true",
            [$"{SmsDeliveryOptions.SectionName}:{nameof(SmsDeliveryOptions.Mode)}"] = "2",
        }).Build();
        var services = new ServiceCollection();
        services.AddCodelabGatewayClient(configuration);

        services.AddNotificationsModule(configuration);

        Assert.Single(services, descriptor => descriptor.ServiceType == typeof(GatewayRelayClient));
    }

    [Fact]
    public void Routes_live_under_the_api_prefix()
    {
        Assert.Equal("api/notifications", NotificationsRoutes.Root);
        Assert.Equal("api/admin/notifications", NotificationsRoutes.Admin);
        Assert.Equal("{id:publicid}/read", NotificationsRoutes.Read);
        Assert.Equal("notifications", NotificationIds.DomainLabel);
    }

    [Fact]
    public void The_kernel_channel_flags_have_their_contract_values()
    {
        Assert.Equal(0, (int)NotificationChannels.None);
        Assert.Equal(1, (int)NotificationChannels.InApp);
        Assert.Equal(2, (int)NotificationChannels.Email);
        Assert.Equal(4, (int)NotificationChannels.Sms);
    }
}
