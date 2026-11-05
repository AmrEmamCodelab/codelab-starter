using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Starter.Application.Authorization;
using Starter.Modules.Access.Domain;
using Starter.Modules.Access.Infrastructure.Persistence.Configurations;

namespace Starter.Modules.Access.Infrastructure.Persistence.Migrations;

/// <summary>
/// Roles and scoped grants: the <c>Roles</c>, <c>RolePermissions</c> and <c>UserRoleAssignments</c> tables, the
/// <c>Scope</c> column of the direct grants (existing grants become <see cref="PermissionScope.Global"/>), and the three
/// virtual roles (<see cref="VirtualRoleSeed"/>). Scaffolded with <c>dotnet ef migrations add</c> and reshaped to the
/// clean-code rules (no generated marker, named lengths and constraint names, short methods); see MODULE.md.
/// </summary>
[DbContext(typeof(AccessDbContext))]
[Migration(AccessMigrationIds.RolesAndGrants)]
public sealed class RolesAndGrants : Migration
{
    private static readonly string[] RolePermissionColumns = [nameof(RolePermissionEntry.RoleId), nameof(RolePermissionEntry.PermissionId)];
    private static readonly string[] UserRoleColumns = [nameof(UserRoleAssignment.UserId), nameof(UserRoleAssignment.RoleId)];

    private static readonly string[] SeedColumns =
    [
        nameof(Role.Code), nameof(Role.NameEn), nameof(Role.NameAr), nameof(Role.IsActive),
        nameof(Role.VirtualKind), nameof(Role.CreatedAt), nameof(Role.UpdatedAt),
    ];

    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        ArgumentNullException.ThrowIfNull(migrationBuilder);
        migrationBuilder.AddColumn<int>(
            name: nameof(UserPermissionGrant.Scope),
            schema: AccessDbContext.SchemaName,
            table: AccessTableNames.UserPermissionGrants,
            type: SqlTypes.EnumColumn,
            nullable: false,
            defaultValue: (int)PermissionScope.Global);
        CreateRoles(migrationBuilder);
        CreateRolePermissions(migrationBuilder);
        CreateUserRoleAssignments(migrationBuilder);
        CreateRoleIndexes(migrationBuilder);
        CreateLinkIndexes(migrationBuilder);
        SeedVirtualRoles(migrationBuilder);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        ArgumentNullException.ThrowIfNull(migrationBuilder);
        migrationBuilder.DropTable(name: AccessTableNames.RolePermissions, schema: AccessDbContext.SchemaName);
        migrationBuilder.DropTable(name: AccessTableNames.UserRoleAssignments, schema: AccessDbContext.SchemaName);
        migrationBuilder.DropTable(name: AccessTableNames.Roles, schema: AccessDbContext.SchemaName);
        migrationBuilder.DropColumn(name: nameof(UserPermissionGrant.Scope), schema: AccessDbContext.SchemaName, table: AccessTableNames.UserPermissionGrants);
    }

    /// <inheritdoc />
    protected override void BuildTargetModel(ModelBuilder modelBuilder) => AccessMigrationModel.Build(modelBuilder);

    private static void CreateRoles(MigrationBuilder migrationBuilder) =>
        migrationBuilder.CreateTable(
            name: AccessTableNames.Roles,
            schema: AccessDbContext.SchemaName,
            columns: table => new
            {
                Id = table.Column<long>(type: SqlTypes.BigInt, nullable: false)
                    .Annotation(SqlTypes.IdentityAnnotation, SqlTypes.IdentitySeedIncrement),
                Code = table.Column<string>(type: SqlTypes.NVarChar(AccessLimits.RoleCodeMaxLength), maxLength: AccessLimits.RoleCodeMaxLength, nullable: false),
                NameEn = table.Column<string>(type: SqlTypes.NVarChar(AccessLimits.RoleNameMaxLength), maxLength: AccessLimits.RoleNameMaxLength, nullable: false),
                NameAr = table.Column<string>(type: SqlTypes.NVarChar(AccessLimits.RoleNameMaxLength), maxLength: AccessLimits.RoleNameMaxLength, nullable: false),
                IsActive = table.Column<bool>(type: SqlTypes.Bit, nullable: false),
                VirtualKind = table.Column<int>(type: SqlTypes.EnumColumn, nullable: true),
                CreatedAt = table.Column<DateTimeOffset>(type: SqlTypes.DateTimeOffset, nullable: false),
                UpdatedAt = table.Column<DateTimeOffset>(type: SqlTypes.DateTimeOffset, nullable: false),
            },
            constraints: table => table.PrimaryKey(AccessMigrationIds.RolesPrimaryKey, role => role.Id));

    private static void CreateRolePermissions(MigrationBuilder migrationBuilder) =>
        migrationBuilder.CreateTable(
            name: AccessTableNames.RolePermissions,
            schema: AccessDbContext.SchemaName,
            columns: table => new
            {
                Id = table.Column<long>(type: SqlTypes.BigInt, nullable: false)
                    .Annotation(SqlTypes.IdentityAnnotation, SqlTypes.IdentitySeedIncrement),
                RoleId = table.Column<long>(type: SqlTypes.BigInt, nullable: false),
                PermissionId = table.Column<long>(type: SqlTypes.BigInt, nullable: false),
            },
            constraints: table =>
            {
                table.PrimaryKey(AccessMigrationIds.RolePermissionsPrimaryKey, granted => granted.Id);
                table.ForeignKey(
                    name: AccessMigrationIds.RolePermissionsPermissionForeignKey,
                    column: granted => granted.PermissionId,
                    principalSchema: AccessDbContext.SchemaName,
                    principalTable: AccessTableNames.Permissions,
                    principalColumn: nameof(PermissionEntry.Id),
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: AccessMigrationIds.RolePermissionsRoleForeignKey,
                    column: granted => granted.RoleId,
                    principalSchema: AccessDbContext.SchemaName,
                    principalTable: AccessTableNames.Roles,
                    principalColumn: nameof(Role.Id),
                    onDelete: ReferentialAction.Cascade);
            });

    private static void CreateUserRoleAssignments(MigrationBuilder migrationBuilder) =>
        migrationBuilder.CreateTable(
            name: AccessTableNames.UserRoleAssignments,
            schema: AccessDbContext.SchemaName,
            columns: table => new
            {
                Id = table.Column<long>(type: SqlTypes.BigInt, nullable: false)
                    .Annotation(SqlTypes.IdentityAnnotation, SqlTypes.IdentitySeedIncrement),
                UserId = table.Column<long>(type: SqlTypes.BigInt, nullable: false),
                RoleId = table.Column<long>(type: SqlTypes.BigInt, nullable: false),
                Scope = table.Column<int>(type: SqlTypes.EnumColumn, nullable: false),
                AssignedAt = table.Column<DateTimeOffset>(type: SqlTypes.DateTimeOffset, nullable: false),
            },
            constraints: table =>
            {
                table.PrimaryKey(AccessMigrationIds.UserRoleAssignmentsPrimaryKey, assignment => assignment.Id);
                table.ForeignKey(
                    name: AccessMigrationIds.UserRoleAssignmentsRoleForeignKey,
                    column: assignment => assignment.RoleId,
                    principalSchema: AccessDbContext.SchemaName,
                    principalTable: AccessTableNames.Roles,
                    principalColumn: nameof(Role.Id),
                    onDelete: ReferentialAction.Restrict);
            });

    private static void CreateRoleIndexes(MigrationBuilder migrationBuilder)
    {
        UniqueIndex(migrationBuilder, AccessMigrationIds.RolesCodeIndex, nameof(Role.Code));
        UniqueIndex(migrationBuilder, AccessMigrationIds.RolesNameArIndex, nameof(Role.NameAr));
        UniqueIndex(migrationBuilder, AccessMigrationIds.RolesNameEnIndex, nameof(Role.NameEn));
        migrationBuilder.CreateIndex(
            name: AccessMigrationIds.RolesVirtualKindIndex,
            schema: AccessDbContext.SchemaName,
            table: AccessTableNames.Roles,
            column: nameof(Role.VirtualKind),
            unique: true,
            filter: RoleConfiguration.VirtualKindFilter);
    }

    private static void CreateLinkIndexes(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateIndex(
            name: AccessMigrationIds.RolePermissionsPermissionIndex,
            schema: AccessDbContext.SchemaName,
            table: AccessTableNames.RolePermissions,
            column: nameof(RolePermissionEntry.PermissionId));
        migrationBuilder.CreateIndex(
            name: AccessMigrationIds.RolePermissionsRolePermissionIndex,
            schema: AccessDbContext.SchemaName,
            table: AccessTableNames.RolePermissions,
            columns: RolePermissionColumns,
            unique: true);
        migrationBuilder.CreateIndex(
            name: AccessMigrationIds.UserRoleAssignmentsRoleIndex,
            schema: AccessDbContext.SchemaName,
            table: AccessTableNames.UserRoleAssignments,
            column: nameof(UserRoleAssignment.RoleId));
        migrationBuilder.CreateIndex(
            name: AccessMigrationIds.UserRoleAssignmentsUserRoleIndex,
            schema: AccessDbContext.SchemaName,
            table: AccessTableNames.UserRoleAssignments,
            columns: UserRoleColumns,
            unique: true);
    }

    private static void UniqueIndex(MigrationBuilder migrationBuilder, string name, string column) =>
        migrationBuilder.CreateIndex(name: name, schema: AccessDbContext.SchemaName, table: AccessTableNames.Roles, column: column, unique: true);

    private static void SeedVirtualRoles(MigrationBuilder migrationBuilder)
    {
        foreach (var row in VirtualRoleSeed.Rows)
        {
            migrationBuilder.InsertData(
                table: AccessTableNames.Roles,
                schema: AccessDbContext.SchemaName,
                columns: SeedColumns,
                values: [row.Code, row.NameEn, row.NameAr, true, (int)row.Kind, VirtualRoleSeed.SeededAt, VirtualRoleSeed.SeededAt]);
        }
    }
}
