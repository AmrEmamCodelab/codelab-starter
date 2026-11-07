using Codelab.Errors; // codelab:module Sms
using Codelab.Integration.Gateway; // codelab:module Sms
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using Starter.Application.Authorization;
using Starter.Application.Notifications;
using Starter.Infrastructure.Messaging;
using Starter.Infrastructure.Persistence;
using Starter.Modules.Notifications.Application;
using Starter.Modules.Notifications.Application.Delivery;
using Starter.Modules.Notifications.Application.Feed;
using Starter.Modules.Notifications.Application.Rendering;
using Starter.Modules.Notifications.Infrastructure.Options;
using Starter.Modules.Notifications.Infrastructure.Persistence;
using Starter.Modules.Notifications.Infrastructure.Sending;

namespace Starter.Modules.Notifications.Infrastructure;

/// <summary>The Notifications module's registrations (called by <see cref="NotificationsModule"/>).</summary>
public static class NotificationsModuleRegistration
{
    /// <summary>
    /// Registers the permission catalog source, the delivery options (validated on start), the DbContext in schema
    /// <c>notifications</c> and its stores, the senders picked by mode (with SMS enabled in gateway mode, also the
    /// integration gateway client of <c>Codelab:Integration:Gateway</c>, which needs <c>AddCodelabSecurity</c> and the S2S
    /// client identity) and the SMS failure classifier, the kernel <see cref="INotificationService"/>, the
    /// two outbox handlers (notification-email, notification-sms) and the feed use cases. The recipients come from the
    /// kernel <see cref="INotificationRecipients"/> port (Identity), the current user from <c>IUserDirectory</c>.
    /// </summary>
    public static IServiceCollection AddNotificationsModule(this IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IPermissionCatalogSource, NotificationsPermissionCatalogSource>());
        services.TryAddSingleton(TimeProvider.System);
        AddOptions(services, configuration);
        services.AddModuleDbContext<NotificationsDbContext>(NotificationsDbContext.SchemaName);
        services.AddScoped<INotificationStore, EfNotificationStore>();
        services.AddScoped<ITemplateStore, EfTemplateStore>();
        AddSenders(services);
        AddGatewaySms(services, configuration); // codelab:module Sms
        AddDelivery(services);
        AddUseCases(services);
        // scaffold:registrations
        return services;
    }

    /// <summary>Maps nothing extra: every Notifications endpoint is a controller, found by the kernel.</summary>
    public static IEndpointRouteBuilder UseNotificationsModule(this IEndpointRouteBuilder endpoints)
    {
        ArgumentNullException.ThrowIfNull(endpoints);
        return endpoints;
    }

    private static void AddOptions(IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<EmailDeliveryOptions>().Bind(configuration.GetSection(EmailDeliveryOptions.SectionName)).ValidateOnStart();
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IValidateOptions<EmailDeliveryOptions>, EmailDeliveryOptionsValidator>());
        services.AddOptions<SmsDeliveryOptions>().Bind(configuration.GetSection(SmsDeliveryOptions.SectionName)).ValidateOnStart();
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IValidateOptions<SmsDeliveryOptions>, SmsDeliveryOptionsValidator>());
        services.AddSingleton(provider => new DeliverySettings(provider.GetRequiredService<IOptions<SmsDeliveryOptions>>().Value.Enabled));
    }

    private static void AddSenders(IServiceCollection services)
    {
        services.AddSingleton<SmtpEmailSender>();
        services.AddSingleton<FileDropEmailSender>();
        services.AddSingleton<FileSmsSender>();
        services.AddSingleton<IEmailSender>(provider =>
            provider.GetRequiredService<IOptions<EmailDeliveryOptions>>().Value.Mode == EmailDeliveryMode.Smtp
                ? provider.GetRequiredService<SmtpEmailSender>()
                : provider.GetRequiredService<FileDropEmailSender>());
        services.AddScoped<ISmsSender>(SelectSmsSender);
    }

    /// <summary>The SMS sender of the configured mode: the gateway sender while it is in use, otherwise the file sink.</summary>
    private static ISmsSender SelectSmsSender(IServiceProvider provider)
    {
        // codelab:module-begin Sms
        if (UsesGateway(provider.GetRequiredService<IOptions<SmsDeliveryOptions>>().Value))
        {
            return provider.GetRequiredService<GatewaySmsSender>();
        }

        // codelab:module-end Sms
        return provider.GetRequiredService<FileSmsSender>();
    }
    // codelab:module-begin Sms

    /// <summary>
    /// Registers the gateway SMS path (the gateway client and <see cref="GatewaySmsSender"/>) when SMS is enabled in
    /// gateway mode, and the SMS failure codes.
    /// </summary>
    private static void AddGatewaySms(IServiceCollection services, IConfiguration configuration)
    {
        if (UsesGateway(ConfiguredSms(configuration)))
        {
            AddGatewayClientOnce(services, configuration);
            services.AddScoped<GatewaySmsSender>();
        }

        services.Configure<CodelabErrorsOptions>(errors => errors.AddSmsDeliveryFailures());
    }

    /// <summary>SMS goes to the integration gateway only while it is enabled and in gateway mode.</summary>
    internal static bool UsesGateway(SmsDeliveryOptions options) => options.Enabled && options.Mode == SmsDeliveryMode.Gateway;

    /// <summary>
    /// The SMS section as configured at registration, read leniently: an unreadable section registers no gateway client
    /// and is refused by the options validation on start.
    /// </summary>
    private static SmsDeliveryOptions ConfiguredSms(IConfiguration configuration)
    {
        try
        {
            return configuration.GetSection(SmsDeliveryOptions.SectionName).Get<SmsDeliveryOptions>() ?? new SmsDeliveryOptions();
        }
        catch (InvalidOperationException)
        {
            return new SmsDeliveryOptions();
        }
    }

    /// <summary>
    /// <c>AddCodelabGatewayClient</c> unless another module (the SSO relay) registered it already: a second call would add
    /// the signing handler twice.
    /// </summary>
    private static void AddGatewayClientOnce(IServiceCollection services, IConfiguration configuration)
    {
        if (!services.Any(descriptor => descriptor.ServiceType == typeof(GatewayRelayClient)))
        {
            services.AddCodelabGatewayClient(configuration);
        }
    }
    // codelab:module-end Sms

    private static void AddDelivery(IServiceCollection services)
    {
        services.AddScoped<NotificationComposer>();
        services.AddScoped<NotificationWriter>();
        services.AddScoped<EmailChannel>();
        services.AddScoped<SmsChannel>();
        services.AddScoped<ImmediateEmails>();
        services.AddScoped<INotificationService, NotificationService>();
        services.AddScoped<NotificationDeliveries>();
        services.AddOutboxHandler<NotificationEmailHandler>();
        services.AddOutboxHandler<NotificationSmsHandler>();
    }

    private static void AddUseCases(IServiceCollection services)
    {
        services.AddScoped<CurrentFeedOwner>();
        services.AddScoped<GetNotificationFeed>();
        services.AddScoped<GetUnreadNotificationCount>();
        services.AddScoped<MarkNotificationRead>();
        services.AddScoped<SendTestNotification>();
    }
}
