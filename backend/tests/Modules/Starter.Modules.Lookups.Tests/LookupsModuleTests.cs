using System.Reflection;
using Codelab.Security.PublicIds;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Starter.Application.Authorization;
using Starter.Application.Validation;
using Starter.Infrastructure.Authorization;
using Starter.Infrastructure.Persistence;
using Starter.Modules.Lookups.Api;
using Starter.Modules.Lookups.Application;
using Starter.Modules.Lookups.Infrastructure;
using Starter.Modules.Lookups.Infrastructure.Caching;
using Starter.Modules.Lookups.Tests.Fakes;

namespace Starter.Modules.Lookups.Tests;

public sealed class LookupsModuleTests
{
    [Fact]
    public void The_module_is_named_after_its_assembly() => Assert.Equal("Lookups", new LookupsModule().Name);

    [Fact]
    public void Every_catalog_key_is_well_formed_unique_and_described()
    {
        var permissions = new LookupsPermissionCatalogSource().Permissions;

        Assert.All(permissions, permission => Assert.True(PermissionKeyFormat.IsValid(permission.Key), permission.Key));
        Assert.All(permissions, permission => Assert.False(string.IsNullOrWhiteSpace(permission.Description)));
        Assert.Equal(permissions.Count, permissions.Select(permission => permission.Key).Distinct(StringComparer.Ordinal).Count());
    }

    [Fact]
    public void Every_declared_key_is_in_the_catalog_and_the_contract_keys_are_declared()
    {
        var declared = typeof(LookupsPermissionKeys).GetFields()
            .Where(field => field.IsLiteral)
            .Select(field => (string)field.GetRawConstantValue()!)
            .Order(StringComparer.Ordinal);
        var catalog = new LookupsPermissionCatalogSource().Permissions.Select(permission => permission.Key).Order(StringComparer.Ordinal);

        Assert.Equal(declared, catalog);
        Assert.Equal(["lookups.manage", "lookups.view"], catalog);
    }

    [Fact]
    public void Registration_declares_the_catalog_source_the_context_the_cache_the_validator_and_the_use_cases()
    {
        var services = new ServiceCollection();

        services.AddLookupsModule(new ConfigurationBuilder().Build());

        Assert.Contains(services, descriptor =>
            descriptor.ServiceType == typeof(IPermissionCatalogSource) && descriptor.ImplementationType == typeof(LookupsPermissionCatalogSource));
        Assert.Contains(services, descriptor => descriptor.ServiceType == typeof(IModuleDatabase));
        Assert.Contains(services, descriptor => descriptor.ServiceType == typeof(IMemoryCache));
        Assert.Contains(services, descriptor => descriptor.ServiceType == typeof(ILookupCache) && descriptor.ImplementationType == typeof(MemoryLookupCache));
        Assert.Contains(services, descriptor => descriptor.ServiceType == typeof(IValidator<SetLookupItemActiveCommand>));
        Type[] useCases = [typeof(GetActiveLookupValues), typeof(ListLookupTypes), typeof(ListLookupItems), typeof(SetLookupItemActive)];
        Assert.All(useCases, useCase => Assert.Contains(services, descriptor => descriptor.ServiceType == useCase));
    }

    [Fact]
    public void Registering_twice_keeps_one_catalog_source() =>
        Assert.Single(
            new ServiceCollection().AddLookupsModule(new ConfigurationBuilder().Build()).AddLookupsModule(new ConfigurationBuilder().Build()),
            descriptor => descriptor.ServiceType == typeof(IPermissionCatalogSource));

    [Fact]
    public void Routes_and_the_id_label_follow_the_contract()
    {
        Assert.Equal("api/lookups", LookupsRoutes.Lookups);
        Assert.Equal("api/admin/lookups", LookupsRoutes.AdminLookups);
        Assert.Equal("{type}", LookupsRoutes.TypeTemplate);
        Assert.Equal("{type}/items/{id:" + PublicIdRouteConstraint.ConstraintName + "}/active", LookupsRoutes.ItemActive);
        Assert.Equal("lookup-items", LookupItemIds.DomainLabel);
    }

    [Fact]
    public void The_read_api_is_for_signed_in_users_and_every_admin_action_needs_a_lookups_permission()
    {
        Assert.NotNull(typeof(LookupsController).GetMethod(nameof(LookupsController.GetAsync))!.GetCustomAttribute<AuthenticatedUserAttribute>());
        var admin = typeof(AdminLookupsController).GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
            .ToDictionary(method => method.Name, method => method.GetCustomAttribute<HasPermissionAttribute>()?.Permission, StringComparer.Ordinal);

        Assert.Equal(LookupsPermissionKeys.View, admin[nameof(AdminLookupsController.ListTypesAsync)]);
        Assert.Equal(LookupsPermissionKeys.View, admin[nameof(AdminLookupsController.ListItemsAsync)]);
        Assert.Equal(LookupsPermissionKeys.Manage, admin[nameof(AdminLookupsController.SetActiveAsync)]);
        Assert.NotNull(typeof(AdminLookupsController).GetMethod(nameof(AdminLookupsController.SetActiveAsync))!.GetCustomAttribute<HttpPutAttribute>());
    }
}

public sealed class LookupsResponsesTests
{
    [Fact]
    public void Values_map_to_the_wire_with_PublicIds_in_order()
    {
        IReadOnlyList<LookupValue> values = [new(2, "request", "طلب", "Request", 20), new(1, "general", "عام", "General", 10)];

        var wire = LookupValueResponse.ListFrom(values);

        Assert.Equal([PublicId<LookupItemIds>.From(2), PublicId<LookupItemIds>.From(1)], wire.Select(value => value.Id));
        Assert.Equal(("request", "طلب", "Request", 20), (wire[0].Code, wire[0].NameAr, wire[0].NameEn, wire[0].SortOrder));
    }

    [Fact]
    public void Admin_items_carry_the_active_flag()
    {
        var item = FakeLookupRepository.Item("other-type", id: 3, "third", sortOrder: 30, isActive: false);

        var wire = Assert.Single(LookupItemResponse.ListFrom([item]));

        Assert.Equal(PublicId<LookupItemIds>.From(3), wire.Id);
        Assert.Equal(("third", "third-ar", "third-en", 30, false), (wire.Code, wire.NameAr, wire.NameEn, wire.SortOrder, wire.IsActive));
    }

    [Fact]
    public void Types_map_their_key_names_and_count() =>
        Assert.Equal(
            new LookupTypeResponse("contact-topics", "مواضيع", "Topics", 4),
            Assert.Single(LookupTypeResponse.ListFrom([new LookupTypeSummary("contact-topics", "مواضيع", "Topics", 4)])));
}
