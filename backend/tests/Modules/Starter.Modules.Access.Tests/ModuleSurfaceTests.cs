using System.Reflection;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Starter.Application.Authorization;
using Starter.Application.Validation;
using Starter.Infrastructure.Authorization;
using Starter.Infrastructure.Persistence;
using Starter.Infrastructure.Startup;
using Starter.Modules.Access.Api;
using Starter.Modules.Access.Application;
using Starter.Modules.Access.Infrastructure;
using Starter.Modules.Access.Infrastructure.Authorization;
using Starter.Modules.Access.Infrastructure.Persistence;
using Starter.Modules.Access.Tests.Fakes;

namespace Starter.Modules.Access.Tests;

public sealed class ModuleSurfaceTests
{
    [Fact]
    public void The_module_is_named_after_its_assembly()
    {
        Assert.Equal("Access", new AccessModule().Name);
    }

    [Theory]
    [InlineData(typeof(DashboardController), AccessPermissionKeys.DashboardView, "api/dashboard")]
    [InlineData(typeof(AccessAdminController), AccessPermissionKeys.AccessAdminView, "api/access/admin")]
    public void Guarded_endpoints_require_their_permission(Type controller, string permission, string route)
    {
        Assert.Equal(permission, Assert.Single(controller.GetCustomAttributes<HasPermissionAttribute>()).Permission);
        Assert.Equal(route, controller.GetCustomAttribute<RouteAttribute>()!.Template);
    }

    [Fact]
    public void My_permissions_is_for_any_signed_in_user()
    {
        var controller = typeof(MyPermissionsController);

        Assert.NotNull(controller.GetCustomAttribute<AuthenticatedUserAttribute>());
        Assert.Empty(controller.GetCustomAttributes<HasPermissionAttribute>());
        Assert.Null(controller.GetCustomAttribute<AllowAnonymousAttribute>());
        Assert.Equal("api/me/permissions", controller.GetCustomAttribute<RouteAttribute>()!.Template);
    }

    [Fact]
    public void Registration_wires_the_ports_the_kernel_and_host_rely_on()
    {
        var services = Registered();

        AssertRegistered<IPermissionCatalogSource, AccessPermissionCatalogSource>(services);
        AssertRegistered<IUserPermissionGrants, UserPermissionGrants>(services);
        AssertRegistered<IUserPermissionResolver, UserPermissionResolver>(services);
        AssertRegistered<IAuthorizationHandler, PermissionHandler>(services);
        AssertRegistered<IStartupTask, PermissionCatalogStartupTask>(services);
        AssertRegistered<IModuleDatabase, ModuleDatabase<AccessDbContext>>(services);
        AssertRegistered<IOrganizationScope, CurrentUserOrganizationScope>(services);
        AssertRegistered<IAdministrationGuard, AdministrationGuard>(services);
        AssertRegistered<IRoleRepository, RoleRepository>(services);
        AssertRegistered<IUserGrantReader, UserGrantReader>(services);
        AssertRegistered<IManagerGraphReader, ManagerGraphReader>(services);
        AssertRegistered<IValidator<CreateRoleCommand>, CreateRoleValidator>(services);
        AssertRegistered<IValidator<UpdateRoleCommand>, UpdateRoleValidator>(services);
        AssertRegistered<IValidator<PutUserGrantsCommand>, PutUserGrantsValidator>(services);
        Assert.All(
            [typeof(ListPermissions), typeof(ListRoles), typeof(GetRole), typeof(CreateRole), typeof(UpdateRole), typeof(GetUserGrants), typeof(PutUserGrants), typeof(SearchAccessUsers)],
            useCase => Assert.Contains(services, descriptor => descriptor.ServiceType == useCase));
    }

    [Theory]
    [InlineData(typeof(AccessPermissionsController), nameof(AccessPermissionsController.List), AccessPermissionKeys.RolesView)]
    [InlineData(typeof(AccessRolesController), nameof(AccessRolesController.List), AccessPermissionKeys.RolesView)]
    [InlineData(typeof(AccessRolesController), nameof(AccessRolesController.Get), AccessPermissionKeys.RolesView)]
    [InlineData(typeof(AccessRolesController), nameof(AccessRolesController.Create), AccessPermissionKeys.RolesManage)]
    [InlineData(typeof(AccessRolesController), nameof(AccessRolesController.Update), AccessPermissionKeys.RolesManage)]
    [InlineData(typeof(AccessUsersController), nameof(AccessUsersController.Search), AccessPermissionKeys.GrantsView)]
    [InlineData(typeof(AccessUsersController), nameof(AccessUsersController.GetGrants), AccessPermissionKeys.GrantsView)]
    [InlineData(typeof(AccessUsersController), nameof(AccessUsersController.PutGrants), AccessPermissionKeys.GrantsManage)]
    public void Editor_actions_require_their_permission(Type controller, string action, string permission) =>
        Assert.Equal(permission, Assert.Single(controller.GetMethod(action)!.GetCustomAttributes<HasPermissionAttribute>()).Permission);

    [Theory]
    [InlineData(typeof(AccessPermissionsController), "api/admin/access/permissions")]
    [InlineData(typeof(AccessRolesController), "api/admin/access/roles")]
    [InlineData(typeof(AccessUsersController), "api/admin/access/users")]
    public void Editor_controllers_live_under_the_admin_access_root(Type controller, string route) =>
        Assert.Equal(route, controller.GetCustomAttribute<RouteAttribute>()!.Template);

    [Fact]
    public void Id_routes_use_the_publicid_constraint()
    {
        Assert.Equal("{id:publicid}", AccessRoutes.RoleIdTemplate);
        Assert.Equal("{userId:publicid}/grants", AccessRoutes.UserGrantsTemplate);
        Assert.Equal("roles", RoleIds.Domain);
        Assert.Equal("users", UserIds.Domain);
    }

    [Fact]
    public void The_catalog_source_describes_every_access_key()
    {
        var keys = new AccessPermissionCatalogSource().Permissions.Select(definition => definition.Key).ToList();

        Assert.Equal(
            [AccessPermissionKeys.DashboardView, AccessPermissionKeys.AccessAdminView, AccessPermissionKeys.RolesView, AccessPermissionKeys.RolesManage, AccessPermissionKeys.GrantsView, AccessPermissionKeys.GrantsManage],
            keys);
        Assert.All(keys, key => Assert.True(PermissionKeyFormat.IsValid(key)));
    }

    [Fact]
    public void The_catalog_singleton_unions_every_registered_source()
    {
        var services = Registered();
        services.AddSingleton<IPermissionCatalogSource>(new StaticCatalogSource(
            new PermissionDefinition(Key: TestCatalogs.ReportsView, Description: "View reports.")));
        using var provider = services.BuildServiceProvider();

        var catalog = provider.GetRequiredService<PermissionCatalog>();

        Assert.True(catalog.Contains(AccessPermissionKeys.DashboardView));
        Assert.True(catalog.Contains(TestCatalogs.ReportsView));
    }

    [Fact]
    public async Task The_catalog_task_synchronizes_in_the_reference_data_phase()
    {
        var rows = new FakePermissionRepository();
        var task = new PermissionCatalogStartupTask(
            new PermissionSynchronizer(TestCatalogs.Default(), rows, new FakeUnitOfWork()),
            NullLogger<PermissionCatalogStartupTask>.Instance);

        await task.RunAsync(CancellationToken.None);

        Assert.Equal(StartupPhase.ReferenceData, task.Phase);
        Assert.Equal(3, rows.Rows.Count);
    }

    [Fact]
    public void The_db_context_uses_the_access_schema()
    {
        Assert.Equal("access", AccessDbContext.SchemaName);
    }

    private static ServiceCollection Registered()
    {
        var services = new ServiceCollection();
        new AccessModule().AddModule(services, new ConfigurationBuilder().Build());
        return services;
    }

    private static void AssertRegistered<TService, TImplementation>(ServiceCollection services) =>
        Assert.Contains(services, descriptor =>
            descriptor.ServiceType == typeof(TService) && descriptor.ImplementationType == typeof(TImplementation));
}
