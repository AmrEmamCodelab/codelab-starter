using System.Reflection;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Starter.Application.Authorization;
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
