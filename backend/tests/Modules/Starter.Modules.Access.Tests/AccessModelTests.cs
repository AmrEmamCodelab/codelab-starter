using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
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
        Assert.Equal([AccessMigrationIds.InitialCreate, AccessMigrationIds.RolesAndGrants], context.Database.GetMigrations());
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
    public void RolesAndGrants_creates_the_tables_keeps_old_grants_global_and_seeds_one_role_per_virtual_kind()
    {
        using var context = NewContext();

        var script = context.GetService<IMigrator>().GenerateScript(fromMigration: AccessMigrationIds.InitialCreate, toMigration: AccessMigrationIds.RolesAndGrants);

        Assert.Contains("CREATE TABLE [access].[Roles]", script, StringComparison.Ordinal);
        Assert.Contains("CREATE TABLE [access].[RolePermissions]", script, StringComparison.Ordinal);
        Assert.Contains("CREATE TABLE [access].[UserRoleAssignments]", script, StringComparison.Ordinal);
        Assert.Contains("ALTER TABLE [access].[UserPermissionGrants] ADD [Scope] int NOT NULL DEFAULT 1", script, StringComparison.Ordinal);
        Assert.Contains("WHERE [VirtualKind] IS NOT NULL", script, StringComparison.Ordinal);
        Assert.All(VirtualRoleSeed.Rows, row => Assert.Contains("N'" + row.Code + "'", script, StringComparison.Ordinal));
        Assert.Equal(Enum.GetValues<VirtualRoleKind>(), VirtualRoleSeed.Rows.Select(row => row.Kind));
        Assert.All(VirtualRoleSeed.Rows, row => Assert.True(RoleCodeFormat.IsValid(row.Code)));
    }

    private static AccessDbContext NewContext() => new(new DbContextOptionsBuilder<AccessDbContext>()
        .UseSqlServer("Server=.;Database=NeverOpened", sql => sql.MigrationsHistoryTable(PersistenceRegistration.MigrationsHistoryTable, AccessDbContext.SchemaName))
        .Options);
}
