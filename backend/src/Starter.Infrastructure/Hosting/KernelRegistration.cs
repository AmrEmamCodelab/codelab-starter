using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using Starter.Application.Auditing;
using Starter.Application.Localization;
using Starter.Application.Users;
using Starter.Infrastructure.Authorization;
using Starter.Infrastructure.Features;
using Starter.Infrastructure.Localization;
using Starter.Infrastructure.Messaging;
using Starter.Infrastructure.Persistence;
using Starter.Infrastructure.Users;

namespace Starter.Infrastructure.Hosting;

/// <summary>The kernel registrations every host needs, independent of the modules.</summary>
public static class KernelRegistration
{
    /// <summary>
    /// Registers persistence (connection string, database options, the startup runner: call this BEFORE
    /// <c>AddCodelabSecurity</c> so migrations run before the security stores ensure their tables), authentication
    /// with a no-credentials default scheme (the Identity module adds the bearer scheme), the HTTP adapters of the
    /// kernel ports (current user, audit context, language), the authorization fallback (authenticated), the feature
    /// switches (<c>Features</c>, validated on start) with the MVC feature gate, the outbox (<c>IOutbox</c> on the
    /// <c>messaging</c> schema, migrated on start with the module contexts), <see cref="TimeProvider"/> and MVC's
    /// request-binding defaults (validators own validation).
    /// </summary>
    public static IServiceCollection AddStarterKernel(this IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        services.TryAddSingleton(TimeProvider.System);
        services.AddHttpContextAccessor();
        services.TryAddScoped<ICurrentUserContext, HttpCurrentUserContext>();
        services.TryAddScoped<IAuditContextAccessor, HttpAuditContextAccessor>();
        services.TryAddScoped<ICurrentLanguage, HttpCurrentLanguage>();
        services.AddStarterPersistence(configuration);
        services.AddStarterMessaging();
        services.AddStarterAuthorization();
        AddFeatures(services, configuration);
        services.Configure<MvcOptions>(options => options.SuppressImplicitRequiredAttributeForNonNullableReferenceTypes = true);
        return services;
    }

    private static void AddFeatures(IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<FeatureSwitchDictionary>()
            .Bind(configuration.GetSection(FeatureSwitchDictionary.SectionName))
            .ValidateOnStart();
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IValidateOptions<FeatureSwitchDictionary>, FeatureSwitchValidator>());
        services.TryAddSingleton<IFeatureGate, FeatureGate>();
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IConfigureOptions<MvcOptions>, FeatureGateMvcSetup>());
    }
}
