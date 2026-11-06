using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Starter.Application.Attachments;
using Starter.Application.Authorization;
using Starter.Application.Records;
using Starter.Application.Workflow;
using Starter.Infrastructure.Auditing;
using Starter.Modules.Records.Api;
using Starter.Modules.Records.Application;
using Starter.Modules.Records.Domain;
using Starter.Modules.Records.Infrastructure;
using Starter.Modules.Records.Infrastructure.Persistence;
using Starter.Modules.Records.Infrastructure.Persistence.Migrations;
using Starter.Modules.Records.Tests.Support;

namespace Starter.Modules.Records.Tests;

public sealed class RecordsModuleTests
{
    [Fact]
    public void The_module_is_named_after_its_assembly()
    {
        Assert.Equal("Records", new RecordsModule().Name);
    }

    [Fact]
    public void Every_catalog_key_is_well_formed_unique_described_and_declared()
    {
        var permissions = new RecordsPermissionCatalogSource().Permissions;
        var declared = typeof(RecordsPermissionKeys).GetFields()
            .Where(field => field.IsLiteral)
            .Select(field => (string)field.GetRawConstantValue()!);

        Assert.All(permissions, permission => Assert.True(PermissionKeyFormat.IsValid(permission.Key), permission.Key));
        Assert.All(permissions, permission => Assert.False(string.IsNullOrWhiteSpace(permission.Description)));
        Assert.Equal(declared.Order(StringComparer.Ordinal), permissions.Select(permission => permission.Key).Order(StringComparer.Ordinal));
        Assert.Equal("records.oversight.view", RecordsPermissionKeys.OversightView);
    }

    [Fact]
    public void Registration_declares_the_catalog_the_kernel_ports_and_the_start_check()
    {
        var services = new ServiceCollection();

        services.AddRecordsModule(new ConfigurationBuilder().Build());

        Assert.Contains(services, descriptor =>
            descriptor.ServiceType == typeof(IPermissionCatalogSource) && descriptor.ImplementationType == typeof(RecordsPermissionCatalogSource));
        Assert.Contains(services, descriptor => descriptor.ServiceType == typeof(IRecordLifecycle) && descriptor.ImplementationType == typeof(RecordLifecycle));
        Assert.Contains(services, descriptor => descriptor.ServiceType == typeof(IRecordDirectory) && descriptor.ImplementationType == typeof(RecordDirectory));
        Assert.Contains(services, descriptor => descriptor.ServiceType == typeof(IHostedService) && descriptor.ImplementationType == typeof(RecordTypesStartupCheck));
        Assert.Contains(services, descriptor => descriptor.ServiceType == typeof(IRecordWorkflowCatalogs));
        Assert.Contains(services, descriptor => descriptor.ServiceType == typeof(IRecordVisibility) && descriptor.ImplementationType == typeof(RecordVisibility));
        Assert.Contains(services, descriptor => descriptor.ServiceType == typeof(IAttachableRecordAccessSource) && descriptor.ImplementationFactory is not null);
    }

    [Fact]
    public async Task The_start_check_fails_on_a_broken_registration()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IRecordTypeRegistrationSource>(new FakeRegistrations(LifecycleTestRecord.Registration));
        services.AddSingleton<IRecordWorkflowCatalogs>(new RecordWorkflowCatalogs([]));
        services.AddSingleton<RecordTypes>();
        await using var provider = services.BuildServiceProvider();

        var failure = await Assert.ThrowsAsync<InvalidOperationException>(() => new RecordTypesStartupCheck(provider).StartingAsync(CancellationToken.None));

        Assert.Contains("'test-record' has no workflow", failure.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Routes_live_under_the_module_root()
    {
        Assert.Equal("api/records", RecordsRoutes.Root);
        Assert.Equal("api/records/{recordType}", RecordsRoutes.RecordType);
        Assert.Equal("records", RecordsIds.DomainLabel);
        Assert.Equal("attachable-records", RecordAttachmentIds.Domain);
        Assert.Equal("users", RecordUserIds.DomainLabel);
    }

    [Fact]
    public void The_model_lives_in_the_records_schema_and_matches_the_InitialCreate_migration()
    {
        using var context = NewContext();

        Assert.Equal(RecordsDbContext.SchemaName, context.Model.GetDefaultSchema());
        Assert.Equal(RecordsTableNames.Records, context.Model.FindEntityType(typeof(Record))!.GetTableName());
        Assert.Equal(RecordsTableNames.RecordHistory, context.Model.FindEntityType(typeof(RecordHistory))!.GetTableName());
        Assert.Equal([RecordsMigrationIds.InitialCreate], context.Database.GetMigrations());
        Assert.False(context.Database.HasPendingModelChanges());
    }

    [Fact]
    public void The_history_points_at_its_record_with_Restrict_and_the_record_has_a_rowversion()
    {
        using var context = NewContext();

        Assert.Empty(RecordHistoryMapping.Violations(context.Model));
        var foreignKey = Assert.Single(context.Model.FindEntityType(typeof(RecordHistory))!.GetForeignKeys());
        Assert.Equal(typeof(Record), foreignKey.PrincipalEntityType.ClrType);
        Assert.Equal(DeleteBehavior.Restrict, foreignKey.DeleteBehavior);
        Assert.True(context.Model.FindEntityType(typeof(Record))!.FindProperty(nameof(Record.RowVersion))!.IsConcurrencyToken);
    }

    [Fact]
    public void The_migration_script_creates_both_tables_the_restrict_key_and_the_filtered_number_index()
    {
        using var context = NewContext();

        var script = context.GetService<IMigrator>().GenerateScript();

        Assert.Contains("CREATE TABLE [records].[Records]", script, StringComparison.Ordinal);
        Assert.Contains("CREATE TABLE [records].[RecordHistory]", script, StringComparison.Ordinal);
        Assert.Contains($"CONSTRAINT [{RecordsMigrationIds.HistoryRecordForeignKey}] FOREIGN KEY", script, StringComparison.Ordinal);
        Assert.DoesNotContain("ON DELETE CASCADE", script, StringComparison.Ordinal);
        Assert.Contains($"CREATE UNIQUE INDEX [{RecordsMigrationIds.NumberIndex}]", script, StringComparison.Ordinal);
        Assert.Contains("WHERE [Number] IS NOT NULL", script, StringComparison.Ordinal);
        Assert.Contains("[RowVersion] rowversion NOT NULL", script, StringComparison.Ordinal);
        Assert.Contains("[records].[__EFMigrationsHistory]", script, StringComparison.Ordinal);
    }

    private static RecordsDbContext NewContext() => new RecordsDesignTimeDbContextFactory().CreateDbContext([]);
}
