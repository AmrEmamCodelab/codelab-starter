using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;
using Starter.Application.Authorization;
using Starter.Application.Settings;
using Starter.Application.Validation;
using Starter.Infrastructure.Persistence;
using Starter.Modules.Settings.Api;
using Starter.Modules.Settings.Application;
using Starter.Modules.Settings.Application.Admin;
using Starter.Modules.Settings.Application.Reading;
using Starter.Modules.Settings.Infrastructure;
using Starter.Modules.Settings.Infrastructure.Options;

namespace Starter.Modules.Settings.Tests;

public sealed class SettingsModuleTests
{
    [Fact]
    public void The_module_is_named_after_its_assembly() => Assert.Equal("Settings", new SettingsModule().Name);

    [Fact]
    public void Every_catalog_key_is_well_formed_unique_and_described()
    {
        var permissions = new SettingsPermissionCatalogSource().Permissions;

        Assert.All(permissions, permission => Assert.True(PermissionKeyFormat.IsValid(permission.Key), permission.Key));
        Assert.All(permissions, permission => Assert.False(string.IsNullOrWhiteSpace(permission.Description)));
        Assert.Equal(permissions.Count, permissions.Select(permission => permission.Key).Distinct(StringComparer.Ordinal).Count());
    }

    [Fact]
    public void The_contract_keys_are_declared_and_in_the_catalog()
    {
        var declared = typeof(SettingsPermissionKeys).GetFields()
            .Where(field => field.IsLiteral)
            .Select(field => (string)field.GetRawConstantValue()!)
            .Order(StringComparer.Ordinal);

        Assert.Equal(["settings.manage", "settings.view"], declared);
        Assert.Equal(declared, new SettingsPermissionCatalogSource().Permissions.Select(permission => permission.Key).Order(StringComparer.Ordinal));
    }

    [Fact]
    public void Routes_follow_the_contract()
    {
        Assert.Equal("api/admin/settings", SettingsRoutes.Admin);
        Assert.Equal("{key}", SettingsRoutes.KeyTemplate);
    }

    [Fact]
    public void Registration_declares_the_catalog_the_context_the_reader_the_validator_and_the_use_cases()
    {
        var services = new ServiceCollection();

        services.AddSettingsModule(new ConfigurationBuilder().Build());

        Assert.Contains(services, descriptor =>
            descriptor.ServiceType == typeof(IPermissionCatalogSource) && descriptor.ImplementationType == typeof(SettingsPermissionCatalogSource));
        Assert.Contains(services, descriptor => descriptor.ServiceType == typeof(IModuleDatabase));
        Assert.Contains(services, descriptor => descriptor.ServiceType == typeof(IValidator<UpdateSettingRequest>));
        Assert.Contains(services, descriptor => descriptor.ServiceType == typeof(UpdateSetting));
        Assert.Contains(services, descriptor => descriptor.ServiceType == typeof(ListSettings));
        Assert.Contains(services, descriptor =>
            descriptor.ServiceType == typeof(IRuntimeSettings) && descriptor.Lifetime == ServiceLifetime.Scoped);
        Assert.Contains(services, descriptor =>
            descriptor.ServiceType == typeof(SettingsSnapshotCache) && descriptor.Lifetime == ServiceLifetime.Singleton);
    }

    [Fact]
    public void A_test_host_can_supply_its_own_clock_and_cache()
    {
        var clock = new FakeTimeProvider();
        var cache = new SettingsSnapshotCache();
        var services = new ServiceCollection().AddSingleton<TimeProvider>(clock).AddSingleton(cache);

        services.AddSettingsModule(new ConfigurationBuilder().Build());
        using var provider = services.BuildServiceProvider();

        Assert.Same(clock, provider.GetRequiredService<TimeProvider>());
        Assert.Same(cache, provider.GetRequiredService<SettingsSnapshotCache>());
    }

    [Theory]
    [InlineData(null, true)]
    [InlineData("true", true)]
    [InlineData("false", false)]
    public void The_stamp_check_is_on_unless_configured_off(string? configured, bool expected)
    {
        var values = new Dictionary<string, string?>(StringComparer.Ordinal);
        if (configured is not null)
        {
            values[$"{SettingsOptions.SectionName}:{nameof(SettingsOptions.CheckStampPerScope)}"] = configured;
        }

        var services = new ServiceCollection();
        services.AddSettingsModule(new ConfigurationBuilder().AddInMemoryCollection(values).Build());
        using var provider = services.BuildServiceProvider();

        Assert.Equal(new SettingsReadPolicy(expected), provider.GetRequiredService<SettingsReadPolicy>());
    }
}
