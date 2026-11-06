using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using Starter.Application.Authorization;
using Starter.Application.Settings;
using Starter.Application.Validation;
using Starter.Infrastructure.Persistence;
using Starter.Modules.Settings.Application;
using Starter.Modules.Settings.Application.Admin;
using Starter.Modules.Settings.Application.Reading;
using Starter.Modules.Settings.Infrastructure.Options;
using Starter.Modules.Settings.Infrastructure.Persistence;

namespace Starter.Modules.Settings.Infrastructure;

/// <summary>The Settings module's registrations (called by <see cref="SettingsModule"/>).</summary>
public static class SettingsModuleRegistration
{
    /// <summary>
    /// Registers the permission catalog source, the <c>Settings</c> options, the DbContext in schema <c>settings</c>
    /// and its stores, the node's snapshot cache (a singleton, added with TryAdd so a test host can supply its own),
    /// the scoped <see cref="IRuntimeSettings"/> reader, the validator and the admin use cases. The clock is the
    /// <see cref="TimeProvider"/> in the container (TryAdd: a test host may replace it).
    /// </summary>
    public static IServiceCollection AddSettingsModule(this IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IPermissionCatalogSource, SettingsPermissionCatalogSource>());
        services.TryAddSingleton(TimeProvider.System);
        services.AddOptions<SettingsOptions>().Bind(configuration.GetSection(SettingsOptions.SectionName));
        services.TryAddSingleton(provider => provider.GetRequiredService<IOptions<SettingsOptions>>().Value.ToPolicy());
        services.AddModuleDbContext<SettingsDbContext>(SettingsDbContext.SchemaName);
        services.AddScoped<ISettingsUnitOfWork>(provider => provider.GetRequiredService<SettingsDbContext>());
        services.AddScoped<ISettingsRepository, SettingsRepository>();
        services.AddScoped<ISettingsSnapshotSource, EfSettingsSnapshotSource>();
        services.TryAddSingleton<SettingsSnapshotCache>();
        services.AddScoped<IRuntimeSettings, RuntimeSettingsReader>();
        services.AddSingleton<IValidator<UpdateSettingRequest>, UpdateSettingRequestValidator>();
        services.AddScoped<SettingChangeMarks>();
        services.AddScoped<ListSettings>();
        services.AddScoped<UpdateSetting>();
        // scaffold:registrations
        return services;
    }

    /// <summary>Maps nothing extra: every Settings endpoint is a controller, found by the kernel.</summary>
    public static IEndpointRouteBuilder UseSettingsModule(this IEndpointRouteBuilder endpoints)
    {
        ArgumentNullException.ThrowIfNull(endpoints);
        return endpoints;
    }
}
