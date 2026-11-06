using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using Starter.Application.Authorization;
using Starter.Application.Validation;
using Starter.Infrastructure.Persistence;
using Starter.Modules.Portal.Application;
using Starter.Modules.Portal.Application.Contact;
using Starter.Modules.Portal.Application.Content;
using Starter.Modules.Portal.Infrastructure.Caching;
using Starter.Modules.Portal.Infrastructure.Lookups;
using Starter.Modules.Portal.Infrastructure.Options;
using Starter.Modules.Portal.Infrastructure.Persistence;

namespace Starter.Modules.Portal.Infrastructure;

/// <summary>The Portal module's registrations (called by <see cref="PortalModule"/>).</summary>
public static class PortalModuleRegistration
{
    /// <summary>
    /// Registers the permission catalog source, the contact inbox options (validated on start), the DbContext in schema
    /// <c>portal</c> and its stores, the contact topics reader, the content cache and the use cases. The reference
    /// numbers come from the kernel <c>IRequestNumberAllocator</c>, the inbox e-mail from the kernel
    /// <c>INotificationService</c> (Notifications module), the contact topics from the kernel <c>ILookupValues</c>
    /// (Lookups module) and the contact switch from the kernel <c>IRuntimeSettings</c> (Settings module).
    /// </summary>
    public static IServiceCollection AddPortalModule(this IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IPermissionCatalogSource, PortalPermissionCatalogSource>());
        services.TryAddSingleton(TimeProvider.System);
        services.AddMemoryCache();
        AddOptions(services, configuration);
        services.AddModuleDbContext<PortalDbContext>(PortalDbContext.SchemaName);
        services.AddScoped<IContactMessageStore, EfContactMessageStore>();
        services.AddScoped<IPortalContentStore, EfPortalContentStore>();
        services.AddScoped<IContactTopics, LookupContactTopics>();
        services.AddSingleton<IPortalContentCache, MemoryPortalContentCache>();
        AddUseCases(services);
        // scaffold:registrations
        return services;
    }

    /// <summary>Maps nothing extra: every Portal endpoint is a controller, found by the kernel.</summary>
    public static IEndpointRouteBuilder UsePortalModule(this IEndpointRouteBuilder endpoints)
    {
        ArgumentNullException.ThrowIfNull(endpoints);
        return endpoints;
    }

    private static void AddOptions(IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<PortalContactOptions>().Bind(configuration.GetSection(PortalContactOptions.SectionName)).ValidateOnStart();
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IValidateOptions<PortalContactOptions>, PortalContactOptionsValidator>());
        services.AddSingleton(provider =>
        {
            var options = provider.GetRequiredService<IOptions<PortalContactOptions>>().Value;
            return new ContactInbox(options.InboxEmail.Trim(), options.InboxLanguage);
        });
    }

    private static void AddUseCases(IServiceCollection services)
    {
        services.AddSingleton<IValidator<SubmitContactMessageCommand>, SubmitContactMessageValidator>();
        services.AddSingleton<IValidator<ReplacePortalContentCommand>, ReplacePortalContentValidator>();
        services.AddScoped<ContactFiling>();
        services.AddScoped<ContactAnnouncer>();
        services.AddScoped<SubmitContactMessage>();
        services.AddScoped<PortalContentReader>();
        services.AddScoped<GetPublishedPortalContent>();
        services.AddScoped<GetPortalContent>();
        services.AddScoped<ReplacePortalContent>();
    }
}
