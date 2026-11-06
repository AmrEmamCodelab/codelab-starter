using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Starter.Modules.Assignments.Domain;
using Starter.Modules.Assignments.Infrastructure.Persistence;
using Starter.Modules.Assignments.Infrastructure.Persistence.Migrations;

namespace Starter.Modules.Assignments.Tests.Infrastructure;

public sealed class AssignmentsModelTests
{
    [Fact]
    public void The_model_lives_in_the_assignments_schema_and_matches_the_InitialCreate_migration()
    {
        using var context = NewContext();

        Assert.Equal(AssignmentsDbContext.SchemaName, context.Model.GetDefaultSchema());
        Assert.Equal(AssignmentsTableNames.RecordAssignments, context.Model.FindEntityType(typeof(RecordAssignment))!.GetTableName());
        Assert.Equal([AssignmentsMigrationIds.InitialCreate], context.Database.GetMigrations());
        Assert.False(context.Database.HasPendingModelChanges());
    }

    [Fact]
    public void The_release_reason_is_an_int_column_without_a_converter()
    {
        using var context = NewContext();
        var column = context.Model.FindEntityType(typeof(RecordAssignment))!.FindProperty(nameof(RecordAssignment.ReleaseReason))!;

        Assert.Equal(AssignmentsSqlTypes.Int32Column, column.GetColumnType());
        Assert.Null(column.GetValueConverter());
    }

    [Fact]
    public void At_most_one_open_row_per_record_is_a_filtered_unique_index()
    {
        using var context = NewContext();
        var index = context.Model.FindEntityType(typeof(RecordAssignment))!.GetIndexes()
            .Single(candidate => candidate.GetDatabaseName() == AssignmentsTableNames.OneOpenRowPerRecordIndex);

        Assert.True(index.IsUnique);
        Assert.Equal(AssignmentsTableNames.OpenRowFilter, index.GetFilter());
        Assert.Equal([nameof(RecordAssignment.RecordId)], index.Properties.Select(property => property.Name));
    }

    [Fact]
    public void The_derived_flag_is_not_mapped()
    {
        using var context = NewContext();

        Assert.Null(context.Model.FindEntityType(typeof(RecordAssignment))!.FindProperty(nameof(RecordAssignment.IsOpen)));
    }

    [Fact]
    public void The_migration_script_creates_the_table_and_its_indexes_in_the_assignments_schema()
    {
        using var context = NewContext();

        var script = context.GetService<IMigrator>().GenerateScript();

        Assert.Contains("[assignments].[RecordAssignments]", script, StringComparison.Ordinal);
        Assert.Contains(AssignmentsTableNames.OneOpenRowPerRecordIndex, script, StringComparison.Ordinal);
        Assert.Contains(AssignmentsTableNames.OpenByAssigneeIndex, script, StringComparison.Ordinal);
        Assert.Contains(AssignmentsTableNames.HistoryIndex, script, StringComparison.Ordinal);
        Assert.Contains("WHERE [ReleasedAt] IS NULL", script, StringComparison.Ordinal);
        Assert.Contains("[assignments].[__EFMigrationsHistory]", script, StringComparison.Ordinal);
    }

    private static AssignmentsDbContext NewContext() => new(new DbContextOptionsBuilder<AssignmentsDbContext>()
        .UseSqlServer("Server=.;Database=NeverOpened", sql => sql.MigrationsHistoryTable(HistoryRepository.DefaultTableName, AssignmentsDbContext.SchemaName))
        .Options);
}
