using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Starter.Modules.ServiceRequests.Domain;
using Starter.Modules.ServiceRequests.Infrastructure.Persistence;
using Starter.Modules.ServiceRequests.Infrastructure.Persistence.Migrations;

namespace Starter.Modules.ServiceRequests.Tests.Infrastructure;

/// <summary>The model and its one migration, without a database (the design-time context never connects).</summary>
public sealed class PersistenceModelTests
{
    private static ServiceRequestsDbContext Context() => new ServiceRequestsDesignTimeDbContextFactory().CreateDbContext([]);

    [Fact]
    public void The_table_lives_in_the_module_schema_keyed_by_the_record_id()
    {
        using var context = Context();
        var entity = context.Model.FindEntityType(typeof(ServiceRequest))!;

        Assert.Equal(ServiceRequestsDbContext.SchemaName, entity.GetSchema());
        Assert.Equal(ServiceRequestsTableNames.ServiceRequests, entity.GetTableName());
        Assert.Equal([nameof(ServiceRequest.RecordId)], entity.FindPrimaryKey()!.Properties.Select(property => property.Name));
        Assert.Empty(entity.GetForeignKeys());
        Assert.Equal(Microsoft.EntityFrameworkCore.Metadata.ValueGenerated.Never, entity.FindProperty(nameof(ServiceRequest.RecordId))!.ValueGenerated);
    }

    [Fact]
    public void There_is_one_initial_migration_and_no_pending_model_change()
    {
        using var context = Context();

        Assert.Equal([ServiceRequestsMigrationIds.InitialCreate], context.Database.GetMigrations());
        Assert.False(context.Database.HasPendingModelChanges());
    }

    [Fact]
    public void The_script_creates_the_table_with_bounded_nullable_columns()
    {
        using var context = Context();

        var script = context.GetService<IMigrator>().GenerateScript();

        Assert.Contains("CREATE TABLE [servicerequests].[ServiceRequests]", script, StringComparison.Ordinal);
        Assert.Contains($"[Title] nvarchar({ServiceRequestsLimits.TitleMaxLength}) NULL", script, StringComparison.Ordinal);
        Assert.Contains($"[Description] nvarchar({ServiceRequestsLimits.DescriptionMaxLength}) NULL", script, StringComparison.Ordinal);
        Assert.Contains($"[ContactPhone] nvarchar({ServiceRequestsLimits.ContactPhoneLength}) NULL", script, StringComparison.Ordinal);
        Assert.Contains("[PreferredDate] date NULL", script, StringComparison.Ordinal);
        Assert.Contains("[RecordId] bigint NOT NULL", script, StringComparison.Ordinal);
        Assert.DoesNotContain("IDENTITY", script, StringComparison.Ordinal);
    }

    [Fact]
    public void The_personal_fields_are_marked_for_the_audit_mask()
    {
        Assert.NotNull(typeof(ServiceRequest).GetProperty(nameof(ServiceRequest.ContactPhone))!.GetCustomAttributes(typeof(Starter.Domain.Auditing.PersonalDataAttribute), inherit: false).SingleOrDefault());
        Assert.NotNull(typeof(ServiceRequest).GetProperty(nameof(ServiceRequest.Description))!.GetCustomAttributes(typeof(Starter.Domain.Auditing.PersonalDataAttribute), inherit: false).SingleOrDefault());
    }

    [Fact]
    public void A_row_needs_a_real_record_id()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => ServiceRequest.StartFor(0));
    }
}
