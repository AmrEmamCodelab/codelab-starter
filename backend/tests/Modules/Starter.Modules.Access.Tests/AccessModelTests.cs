using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Operations;
using Starter.Infrastructure.Persistence;
using Starter.Modules.Access.Domain;
using Starter.Modules.Access.Infrastructure.Persistence;
using Starter.Modules.Access.Infrastructure.Persistence.Configurations;
using Starter.Modules.Access.Infrastructure.Persistence.Migrations;

namespace Starter.Modules.Access.Tests;

public sealed class AccessModelTests
{
    [Fact]
    public void The_model_matches_the_migrations_and_has_no_pending_change()
    {
        using var context = NewContext();

        Assert.Equal(AccessDbContext.SchemaName, context.Model.GetDefaultSchema());
        Assert.Equal([AccessMigrationIds.InitialCreate], context.Database.GetMigrations());
        Assert.False(context.Database.HasPendingModelChanges());
    }

    [Theory]
    [InlineData(typeof(Role), nameof(Role.VirtualKind))]
    [InlineData(typeof(UserRoleAssignment), nameof(UserRoleAssignment.Scope))]
    [InlineData(typeof(UserPermissionGrant), nameof(UserPermissionGrant.Scope))]
    public void Enums_are_int_columns_without_a_converter(Type entity, string property)
    {
        using var context = NewContext();

        var column = context.Model.FindEntityType(entity)!.FindProperty(property)!;

        Assert.Equal(SqlTypes.EnumColumn, column.GetColumnType());
        Assert.Null(column.GetValueConverter());
    }

    [Fact]
    public void The_tables_live_in_the_access_schema()
    {
        using var context = NewContext();

        Assert.Equal(AccessTableNames.Roles, context.Model.FindEntityType(typeof(Role))!.GetTableName());
        Assert.Equal(AccessTableNames.RolePermissions, context.Model.FindEntityType(typeof(RolePermissionEntry))!.GetTableName());
        Assert.Equal(AccessTableNames.UserRoleAssignments, context.Model.FindEntityType(typeof(UserRoleAssignment))!.GetTableName());
        Assert.Null(context.Model.FindEntityType(typeof(Role))!.FindProperty(nameof(Role.IsVirtual)));
    }

    [Fact]
    public void The_one_migration_creates_every_table_with_scoped_grants_and_seeds_one_role_per_virtual_kind()
    {
        using var context = NewContext();

        var script = context.GetService<IMigrator>().GenerateScript();

        Assert.Contains("CREATE TABLE [access].[Permissions]", script, StringComparison.Ordinal);
        Assert.Contains("CREATE TABLE [access].[UserPermissionGrants]", script, StringComparison.Ordinal);
        Assert.Contains("CREATE TABLE [access].[Roles]", script, StringComparison.Ordinal);
        Assert.Contains("CREATE TABLE [access].[RolePermissions]", script, StringComparison.Ordinal);
        Assert.Contains("CREATE TABLE [access].[UserRoleAssignments]", script, StringComparison.Ordinal);
        Assert.Contains("[Scope] int NOT NULL", script, StringComparison.Ordinal);
        Assert.Contains("WHERE [VirtualKind] IS NOT NULL", script, StringComparison.Ordinal);
        Assert.Contains(AccessMigrationIds.InitialCreate, script, StringComparison.Ordinal);
        Assert.All(VirtualRoleSeed.Rows, row => Assert.Contains("N'" + row.Code + "'", script, StringComparison.Ordinal));
        Assert.Equal(Enum.GetValues<VirtualRoleKind>(), VirtualRoleSeed.Rows.Select(row => row.Kind));
        Assert.All(VirtualRoleSeed.Rows, row => Assert.True(RoleCodeFormat.IsValid(row.Code)));
    }

    [Fact]
    public void The_one_migration_carries_every_operation_of_the_model_diff_and_seeds_only_the_virtual_roles()
    {
        using var context = NewContext();
        var migration = new InitialCreate();
        var target = context.GetService<IModelRuntimeInitializer>().Initialize(migration.TargetModel!, designTime: true);

        var expected = context.GetService<IMigrationsModelDiffer>().GetDifferences(source: null, target.GetRelationalModel());
        var schema = migration.UpOperations.Where(operation => operation is not InsertDataOperation).ToList();
        var seeds = migration.UpOperations.OfType<InsertDataOperation>().ToList();

        Assert.Equal(expected.Select(Describe).Order(StringComparer.Ordinal), schema.Select(Describe).Order(StringComparer.Ordinal));
        Assert.Equal(VirtualRoleSeed.Rows.Count, seeds.Count);
        Assert.All(seeds, seed => Assert.Equal(AccessTableNames.Roles, seed.Table));
    }

    private static string Describe(MigrationOperation operation) => operation switch
    {
        CreateIndexOperation index =>
            $"index {index.Table}.{index.Name} ({string.Join(",", index.Columns)}) unique={index.IsUnique} filter={index.Filter}",
        CreateTableOperation table =>
            $"table {table.Name} ({string.Join(",", table.Columns.OrderBy(column => column.Name, StringComparer.Ordinal).Select(column => $"{column.Name}:{column.ColumnType}:{column.IsNullable}"))}) "
            + $"fk=({string.Join(",", table.ForeignKeys.OrderBy(key => key.Name, StringComparer.Ordinal).Select(key => $"{key.Name}:{key.OnDelete}"))})",
        EnsureSchemaOperation ensure => $"schema {ensure.Name}",
        _ => operation.GetType().Name,
    };

    private static AccessDbContext NewContext() => new(new DbContextOptionsBuilder<AccessDbContext>()
        .UseSqlServer("Server=.;Database=NeverOpened", sql => sql.MigrationsHistoryTable(PersistenceRegistration.MigrationsHistoryTable, AccessDbContext.SchemaName))
        .Options);
}
