using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Starter.Infrastructure.Persistence;
using Starter.Modules.Ops.Domain;
using Starter.Modules.Ops.Infrastructure.Persistence;
using Starter.Modules.Ops.Infrastructure.Persistence.Migrations;

namespace Starter.Modules.Ops.Tests.Infrastructure;

public sealed class PersistenceTests
{
    [Fact]
    public void The_model_lives_in_the_ops_schema_and_matches_the_InitialCreate_migration()
    {
        using var context = NewContext();

        Assert.Equal(OpsDbContext.SchemaName, context.Model.GetDefaultSchema());
        Assert.Equal(OpsTableNames.OperatorAccess, context.Model.FindEntityType(typeof(OperatorAccess))!.GetTableName());
        Assert.Equal(OpsTableNames.ReferenceCodeSightings, context.Model.FindEntityType(typeof(ReferenceCodeSighting))!.GetTableName());
        Assert.Equal([OpsMigrationIds.InitialCreate], context.Database.GetMigrations());
        Assert.False(context.Database.HasPendingModelChanges());
    }

    [Fact]
    public void The_endpoint_enum_is_an_int_column_without_a_converter()
    {
        using var context = NewContext();
        var column = context.Model.FindEntityType(typeof(OperatorAccess))!.FindProperty(nameof(OperatorAccess.Endpoint))!;

        Assert.Equal(OpsSqlTypes.Int32Column, column.GetColumnType());
        Assert.Null(column.GetValueConverter());
    }

    [Fact]
    public void The_migration_script_creates_both_tables_and_their_indexes_in_the_ops_schema()
    {
        using var context = NewContext();

        var script = context.GetService<IMigrator>().GenerateScript();

        Assert.Contains("[ops].[OperatorAccess]", script, StringComparison.Ordinal);
        Assert.Contains("[ops].[ReferenceCodeSightings]", script, StringComparison.Ordinal);
        Assert.Contains(OpsTableNames.ReferenceCodeIdIndex, script, StringComparison.Ordinal);
        Assert.Contains(OpsTableNames.OccurredAtIndex, script, StringComparison.Ordinal);
        Assert.Contains(OpsTableNames.OperatorAccessAtIndex, script, StringComparison.Ordinal);
        Assert.Contains("[ops].[__EFMigrationsHistory]", script, StringComparison.Ordinal);
    }

    [Fact]
    public void The_design_time_factory_builds_the_context_on_the_ops_history_table()
    {
        using var context = new OpsDesignTimeDbContextFactory().CreateDbContext([]);

        Assert.Equal(OpsDbContext.SchemaName, context.Model.GetDefaultSchema());
    }

    private static OpsDbContext NewContext() => new(new DbContextOptionsBuilder<OpsDbContext>()
        .UseSqlServer(
            "Server=(localdb)\\unused;Database=unused;Trusted_Connection=True",
            sql => sql.MigrationsHistoryTable(PersistenceRegistration.MigrationsHistoryTable, OpsDbContext.SchemaName))
        .Options);
}
