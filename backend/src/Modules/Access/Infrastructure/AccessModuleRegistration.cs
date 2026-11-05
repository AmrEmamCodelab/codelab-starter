using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Starter.Application.Authorization;
using Starter.Application.Validation;
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
    /// DbContext and stores, the live resolver, the kernel <see cref="IOrganizationScope"/> and <see cref="IAdministrationGuard"/>, the
    /// <c>[HasPermission]</c> handler, the kernel grants port, the catalog startup task, the role and grant editor
    /// with its guard, and the use cases. The editor reads accounts through the kernel <c>IAccountAdministration</c>
    /// port, which the Identity module registers.
    /// </summary>
    public static IServiceCollection AddAccessModule(this IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IPermissionCatalogSource, AccessPermissionCatalogSource>());
        services.TryAddSingleton<PermissionCatalog>();
        services.TryAddSingleton(TimeProvider.System);
        AddPersistence(services);
        AddResolve(services);
        AddEditor(services);
        return services;
    }

    /// <summary>Maps nothing extra: every Access endpoint is a controller, found by the kernel.</summary>
    public static IEndpointRouteBuilder UseAccessModule(this IEndpointRouteBuilder endpoints)
    {
        ArgumentNullException.ThrowIfNull(endpoints);
        return endpoints;
    }

    private static void AddPersistence(IServiceCollection services)
    {
        services.AddModuleDbContext<AccessDbContext>(AccessDbContext.SchemaName);
        services.AddScoped<IAccessUnitOfWork>(provider => provider.GetRequiredService<AccessDbContext>());
        services.AddScoped<IPermissionRepository, PermissionRepository>();
        services.AddScoped<IUserPermissionGrantRepository, UserPermissionGrantRepository>();
        services.AddScoped<IRoleRepository, RoleRepository>();
        services.AddScoped<IUserRoleAssignmentRepository, UserRoleAssignmentRepository>();
        services.AddScoped<IUserGrantReader, UserGrantReader>();
        services.AddScoped<IManagerGraphReader, ManagerGraphReader>();
    }

    private static void AddResolve(IServiceCollection services)
    {
        services.AddScoped<IUserPermissionResolver, UserPermissionResolver>();
        services.AddScoped<IOrganizationScope, CurrentUserOrganizationScope>();
        services.AddScoped<IAdministrationGuard, AdministrationGuard>();
        services.AddScoped<IUserPermissionGrants, UserPermissionGrants>();
        services.AddScoped<IAuthorizationHandler, PermissionHandler>();
        services.AddScoped<PermissionSynchronizer>();
        services.AddScoped<IStartupTask, PermissionCatalogStartupTask>();
        services.AddScoped<GetMyPermissions>();
        services.AddScoped<IDashboardSummaryStore, PlaceholderDashboardSummaryStore>();
        services.AddScoped<IAccessAdminOverviewStore, PlaceholderAccessAdminOverviewStore>();
        services.AddScoped<GetDashboard>();
        services.AddScoped<GetAccessAdminOverview>();
    }

    private static void AddEditor(IServiceCollection services)
    {
        services.AddScoped<IValidator<CreateRoleCommand>, CreateRoleValidator>();
        services.AddScoped<IValidator<UpdateRoleCommand>, UpdateRoleValidator>();
        services.AddScoped<IValidator<PutUserGrantsCommand>, PutUserGrantsValidator>();
        services.AddScoped<AccessChanges>();
        services.AddScoped<GrantablePermissions>();
        services.AddScoped<ManagerContinuityGuard>();
        services.AddScoped<GrantRequestReader>();
        services.AddScoped<GrantCeiling>();
        services.AddScoped<UserGrantWriteGuards>();
        services.AddScoped<UserGrantWriter>();
        services.AddScoped<UserGrantsViewBuilder>();
        services.AddScoped<ListPermissions>();
        services.AddScoped<ListRoles>();
        services.AddScoped<GetRole>();
        services.AddScoped<CreateRole>();
        services.AddScoped<UpdateRole>();
        services.AddScoped<ScopedAccounts>();
        services.AddScoped<GetUserGrants>();
        services.AddScoped<PutUserGrants>();
        services.AddScoped<SearchAccessUsers>();
    }
}
