using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Starter.Application.Authorization;
using Starter.Infrastructure.Persistence;
using Starter.Infrastructure.Startup;
using Starter.Modules.Access.Application;
using Starter.Modules.Access.Infrastructure.Authorization;
using Starter.Modules.Access.Infrastructure.Persistence;

namespace Starter.Modules.Access.Infrastructure;

/// <summary>The Access module's registrations (called by <see cref="AccessModule"/>).</summary>
public static class AccessModuleRegistration
{
    /// <summary>
    /// Registers the catalog (singleton, the union of every <see cref="IPermissionCatalogSource"/>), the Access
    /// DbContext and stores, the live resolver, the <c>[HasPermission]</c> handler, the kernel grants port, the catalog
    /// startup task, the use cases and their (phase 2 placeholder) stores.
    /// </summary>
    public static IServiceCollection AddAccessModule(this IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IPermissionCatalogSource, AccessPermissionCatalogSource>());
        services.TryAddSingleton<PermissionCatalog>();
        services.TryAddSingleton(TimeProvider.System);

        services.AddModuleDbContext<AccessDbContext>(AccessDbContext.SchemaName);
        services.AddScoped<IAccessUnitOfWork>(provider => provider.GetRequiredService<AccessDbContext>());
        services.AddScoped<IPermissionRepository, PermissionRepository>();
        services.AddScoped<IUserPermissionGrantRepository, UserPermissionGrantRepository>();

        services.AddScoped<IUserPermissionResolver, UserPermissionResolver>();
        services.AddScoped<IUserPermissionGrants, UserPermissionGrants>();
        services.AddScoped<IAuthorizationHandler, PermissionHandler>();
        services.AddScoped<PermissionSynchronizer>();
        services.AddScoped<IStartupTask, PermissionCatalogStartupTask>();

        services.AddScoped<GetMyPermissions>();
        services.AddScoped<IDashboardSummaryStore, PlaceholderDashboardSummaryStore>();
        services.AddScoped<IAccessAdminOverviewStore, PlaceholderAccessAdminOverviewStore>();
        services.AddScoped<GetDashboard>();
        services.AddScoped<GetAccessAdminOverview>();
        return services;
    }

    /// <summary>Maps nothing extra: every Access endpoint is a controller, found by the kernel.</summary>
    public static IEndpointRouteBuilder UseAccessModule(this IEndpointRouteBuilder endpoints)
    {
        ArgumentNullException.ThrowIfNull(endpoints);
        return endpoints;
    }
}
