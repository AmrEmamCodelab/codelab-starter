using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Starter.Domain.Auditing;
using Starter.Infrastructure.Auditing;
using Starter.Infrastructure.Auditing.Migrations;
using Starter.Api.Tests.Auditing.Cascading;
using Starter.Infrastructure.Persistence;

namespace Starter.Api.Tests.Auditing;

/// <summary>The audit schema against its migration, and the record history Restrict rule on the model metadata.</summary>
public sealed class AuditModelTests
{
    private const string NeverOpened = "Server=.;Database=NeverOpened";

    [Fact]
    public void The_audit_model_lives_in_the_audit_schema_and_matches_the_InitialCreate_migration()
    {
        using var context = OfflineAudit();

        Assert.Equal(AuditDbContext.SchemaName, context.Model.GetDefaultSchema());
        Assert.Equal(AuditTableNames.AuditEntries, context.Model.FindEntityType(typeof(AuditEntry))!.GetTableName());
        Assert.Equal([AuditMigrationIds.InitialCreate], context.Database.GetMigrations());
        Assert.False(context.Database.HasPendingModelChanges());
    }

    [Fact]
    public void The_action_is_an_int_column_and_the_script_creates_the_table_index_and_history_in_the_audit_schema()
    {
        using var context = OfflineAudit();
        var action = context.Model.FindEntityType(typeof(AuditEntry))!.FindProperty(nameof(AuditEntry.Action))!;

        var script = context.GetService<IMigrator>().GenerateScript();

        Assert.Equal(AuditSqlTypes.Int32Column, action.GetColumnType());
        Assert.Null(action.GetValueConverter());
        Assert.Contains("CREATE TABLE [audit].[AuditEntries]", script, StringComparison.Ordinal);
        Assert.Contains($"[{AuditMigrationIds.AuditEntriesEntityIndex}]", script, StringComparison.Ordinal);
        Assert.Contains($"[audit].[{PersistenceRegistration.MigrationsHistoryTable}]", script, StringComparison.Ordinal);
        Assert.Contains("[ChangesJson] nvarchar(max) NOT NULL", script, StringComparison.Ordinal);
    }

    [Fact]
    public void HasRecordHistory_maps_the_record_foreign_key_with_Restrict_and_the_timeline_index()
    {
        using var context = AuditProbeContext.Offline();
        var history = context.Model.FindEntityType(typeof(ProbeHistory))!;

        var foreignKey = Assert.Single(history.GetForeignKeys());

        Assert.Equal(typeof(ProbePerson), foreignKey.PrincipalEntityType.ClrType);
        Assert.Equal(nameof(RecordHistoryEntry.RecordId), Assert.Single(foreignKey.Properties).Name);
        Assert.Equal(DeleteBehavior.Restrict, foreignKey.DeleteBehavior);
        Assert.True(foreignKey.IsRequired);
        Assert.Contains(history.GetIndexes(), index => index.Properties.Select(property => property.Name).SequenceEqual([nameof(RecordHistoryEntry.RecordId), nameof(RecordHistoryEntry.At)]));
        Assert.Empty(RecordHistoryMapping.Violations(context.Model));
    }

    [Fact]
    public void HasRecordHistory_bounds_the_text_columns()
    {
        using var context = AuditProbeContext.Offline();
        var history = context.Model.FindEntityType(typeof(ProbeHistory))!;

        Assert.Equal(RecordHistoryLimits.ActionMaxLength, history.FindProperty(nameof(RecordHistoryEntry.Action))!.GetMaxLength());
        Assert.Equal(RecordHistoryLimits.ReasonMaxLength, history.FindProperty(nameof(RecordHistoryEntry.Reason))!.GetMaxLength());
        Assert.Equal(RecordHistoryLimits.ActorNameMaxLength, history.FindProperty(nameof(RecordHistoryEntry.ActorName))!.GetMaxLength());
    }

    [Theory]
    [InlineData(DeleteBehavior.Cascade)]
    [InlineData(DeleteBehavior.ClientCascade)]
    [InlineData(DeleteBehavior.SetNull)]
    [InlineData(DeleteBehavior.ClientSetNull)]
    public void A_module_context_whose_history_would_follow_its_record_refuses_to_build_its_model(DeleteBehavior behavior)
    {
        CascadingHistoryContext.Behavior = behavior;
        using var context = new CascadingHistoryContext();

        var refused = Assert.Throws<InvalidOperationException>(() => context.Model);

        Assert.Contains(nameof(ProbeHistory), refused.Message, StringComparison.Ordinal);
        Assert.Contains("Restrict", refused.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void The_guard_reports_every_offending_foreign_key_as_a_line()
    {
        var builder = new ModelBuilder();
        builder.Entity<ProbePerson>();
        builder.Entity<ProbeHistory>().HasOne<ProbePerson>().WithMany().HasForeignKey(entry => entry.RecordId).OnDelete(DeleteBehavior.Cascade);

        var violation = Assert.Single(RecordHistoryMapping.Violations(builder.Model));

        Assert.Contains("Cascade", violation, StringComparison.Ordinal);
    }

    private static AuditDbContext OfflineAudit() => new(new DbContextOptionsBuilder<AuditDbContext>()
        .UseSqlServer(NeverOpened, sql => sql.MigrationsHistoryTable(PersistenceRegistration.MigrationsHistoryTable, AuditDbContext.SchemaName))
        .Options);
}
