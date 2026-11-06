using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Starter.Application.Authorization;
using Starter.Modules.Attachments.Api;
using Starter.Modules.Attachments.Application;
using Starter.Modules.Attachments.Infrastructure;

namespace Starter.Modules.Attachments.Tests;

public sealed class AttachmentsModuleTests
{
    [Fact]
    public void The_module_is_named_after_its_assembly()
    {
        Assert.Equal("Attachments", new AttachmentsModule().Name);
    }

    [Fact]
    public void Every_catalog_key_is_well_formed_unique_and_described()
    {
        var permissions = new AttachmentsPermissionCatalogSource().Permissions;

        Assert.NotEmpty(permissions);
        Assert.All(permissions, permission => Assert.True(PermissionKeyFormat.IsValid(permission.Key), permission.Key));
        Assert.All(permissions, permission => Assert.False(string.IsNullOrWhiteSpace(permission.Description)));
        Assert.Equal(permissions.Count, permissions.Select(permission => permission.Key).Distinct(StringComparer.Ordinal).Count());
    }

    [Fact]
    public void Every_declared_key_is_in_the_catalog()
    {
        var declared = typeof(AttachmentsPermissionKeys).GetFields()
            .Where(field => field.IsLiteral)
            .Select(field => (string)field.GetRawConstantValue()!);
        var catalog = new AttachmentsPermissionCatalogSource().Permissions.Select(permission => permission.Key);

        Assert.Equal(declared.Order(StringComparer.Ordinal), catalog.Order(StringComparer.Ordinal));
    }

    [Fact]
    public void Registration_declares_the_catalog_source()
    {
        var services = new ServiceCollection();

        services.AddAttachmentsModule(new ConfigurationBuilder().Build());

        Assert.Contains(services, descriptor =>
            descriptor.ServiceType == typeof(IPermissionCatalogSource) && descriptor.ImplementationType == typeof(AttachmentsPermissionCatalogSource));
    }

    [Fact]
    public void Routes_live_under_the_module_root()
    {
        Assert.Equal("api/attachments", AttachmentsRoutes.Root);
        Assert.Equal("attachments", AttachmentsIds.DomainLabel);
    }
}
