using Starter.Application.Authorization;
using Starter.Modules.Access.Application;
using Starter.Modules.Access.Domain;
using Starter.Modules.Access.Tests.Fakes;

namespace Starter.Modules.Access.Tests;

public sealed class PermissionCatalogTests
{
    [Fact]
    public void Unions_every_source_sorted_by_key()
    {
        var catalog = new PermissionCatalog(
        [
            new AccessPermissionCatalogSource(),
            new StaticCatalogSource(new PermissionDefinition(Key: TestCatalogs.ReportsView, Description: "View reports.")),
        ]);

        Assert.Equal(
            [
                AccessPermissionKeys.AccessAdminView, AccessPermissionKeys.GrantsManage, AccessPermissionKeys.GrantsView,
                AccessPermissionKeys.RolesManage, AccessPermissionKeys.RolesView, AccessPermissionKeys.DashboardView, TestCatalogs.ReportsView,
            ],
            catalog.Definitions.Select(definition => definition.Key));
        Assert.True(catalog.Contains(TestCatalogs.ReportsView));
        Assert.False(catalog.Contains("reports.delete"));
        Assert.Equal(7, catalog.Keys.Count);
    }

    [Fact]
    public void Refuses_a_key_declared_twice_across_sources()
    {
        var error = Assert.Throws<InvalidOperationException>(() => new PermissionCatalog(
        [
            new AccessPermissionCatalogSource(),
            new StaticCatalogSource(new PermissionDefinition(Key: AccessPermissionKeys.DashboardView, Description: "Again.")),
        ]));

        Assert.Contains(AccessPermissionKeys.DashboardView, error.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("Dashboard.view")]
    [InlineData("dashboard")]
    [InlineData("dashboard..view")]
    [InlineData("dashboard.view.")]
    [InlineData("1dashboard.view")]
    [InlineData("dashboard view")]
    public void Refuses_a_key_that_breaks_the_format(string key)
    {
        var error = Assert.Throws<InvalidOperationException>(() =>
            new PermissionCatalog([new StaticCatalogSource(new PermissionDefinition(Key: key, Description: "Bad."))]));

        Assert.Contains(key, error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Refuses_a_permission_without_a_description()
    {
        Assert.Throws<InvalidOperationException>(() =>
            new PermissionCatalog([new StaticCatalogSource(new PermissionDefinition(Key: TestCatalogs.ReportsView, Description: " "))]));
    }

    [Fact]
    public void An_empty_catalog_is_valid()
    {
        Assert.Empty(new PermissionCatalog([]).Definitions);
    }

    [Fact]
    public void The_access_source_declares_the_phase_three_keys()
    {
        var keys = new AccessPermissionCatalogSource().Permissions.Select(permission => permission.Key);

        Assert.Equal(["dashboard.view", "access.admin.view", "access.roles.view", "access.roles.manage", "access.grants.view", "access.grants.manage"], keys);
    }

    [Fact]
    public void Every_access_key_is_valid_and_described()
    {
        Assert.All(new AccessPermissionCatalogSource().Permissions, permission =>
        {
            Assert.True(PermissionKeyFormat.IsValid(permission.Key));
            Assert.False(string.IsNullOrWhiteSpace(permission.Description));
            Assert.True(permission.Description.Length <= AccessLimits.PermissionDescriptionMaxLength);
        });
    }

    [Fact]
    public void Column_sizes_match_the_kernel_key_format()
    {
        Assert.Equal(PermissionKeyFormat.MaxLength, AccessLimits.PermissionKeyMaxLength);
        Assert.True(AccessLimits.PermissionOwnerMaxLength < AccessLimits.PermissionKeyMaxLength);
    }
}
