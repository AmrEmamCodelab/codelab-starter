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
    /// <c>notifications</c> and its stores, the senders picked by mode, the kernel <see cref="INotificationService"/>, the
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
        services.AddSingleton<GatewaySmsSender>();
        services.AddSingleton<IEmailSender>(provider =>
            provider.GetRequiredService<IOptions<EmailDeliveryOptions>>().Value.Mode == EmailDeliveryMode.Smtp
                ? provider.GetRequiredService<SmtpEmailSender>()
                : provider.GetRequiredService<FileDropEmailSender>());
        services.AddSingleton<ISmsSender>(provider =>
            provider.GetRequiredService<IOptions<SmsDeliveryOptions>>().Value.Mode == SmsDeliveryMode.Gateway
                ? provider.GetRequiredService<GatewaySmsSender>()
                : provider.GetRequiredService<FileSmsSender>());
    }

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
