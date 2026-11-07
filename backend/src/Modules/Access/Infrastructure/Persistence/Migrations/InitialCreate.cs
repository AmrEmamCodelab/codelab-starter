using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Starter.Modules.Access.Domain;
using Starter.Modules.Access.Infrastructure.Persistence.Configurations;

namespace Starter.Modules.Access.Infrastructure.Persistence.Migrations;

/// <summary>
/// The whole Access schema in one migration (ADR-010 squash): the permission catalog, the direct grants with their
/// scope, the roles with their permissions and user assignments, and the three virtual roles
/// (<see cref="VirtualRoleSeed"/>). It keeps the id of the first migration so a database migrated before the squash
/// stays valid. Reshaped to the clean-code rules (no generated marker, named lengths and constraint names, short
/// methods); a unit test checks it against EF's own model diff. See MODULE.md.
/// </summary>
[DbContext(typeof(AccessDbContext))]
[Migration(AccessMigrationIds.InitialCreate)]
public sealed class InitialCreate : Migration
{
    private const string Schema = AccessDbContext.SchemaName;

    private static readonly string[] UserPermissionColumns = [nameof(UserPermissionGrant.UserId), nameof(UserPermissionGrant.PermissionId)];
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
        migrationBuilder.EnsureSchema(name: Schema);
        CreatePermissions(migrationBuilder);
        CreateRoles(migrationBuilder);
        CreateRolePermissions(migrationBuilder);
        CreateUserPermissionGrants(migrationBuilder);
        CreateUserRoleAssignments(migrationBuilder);
        CreateIndexes(migrationBuilder);
        SeedVirtualRoles(migrationBuilder);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        ArgumentNullException.ThrowIfNull(migrationBuilder);
        migrationBuilder.DropTable(name: AccessTableNames.RolePermissions, schema: Schema);
        migrationBuilder.DropTable(name: AccessTableNames.UserPermissionGrants, schema: Schema);
        migrationBuilder.DropTable(name: AccessTableNames.UserRoleAssignments, schema: Schema);
        migrationBuilder.DropTable(name: AccessTableNames.Permissions, schema: Schema);
        migrationBuilder.DropTable(name: AccessTableNames.Roles, schema: Schema);
    }

    /// <inheritdoc />
    protected override void BuildTargetModel(ModelBuilder modelBuilder) => AccessMigrationModel.Build(modelBuilder);

    private static void CreatePermissions(MigrationBuilder migrationBuilder) =>
        migrationBuilder.CreateTable(
            name: AccessTableNames.Permissions,
            schema: Schema,
            columns: table => new
            {
                Id = table.Column<long>(type: SqlTypes.BigInt, nullable: false)
                    .Annotation(SqlTypes.IdentityAnnotation, SqlTypes.IdentitySeedIncrement),
                Key = table.Column<string>(type: SqlTypes.NVarChar(AccessLimits.PermissionKeyMaxLength), maxLength: AccessLimits.PermissionKeyMaxLength, nullable: false),
                Owner = table.Column<string>(type: SqlTypes.NVarChar(AccessLimits.PermissionOwnerMaxLength), maxLength: AccessLimits.PermissionOwnerMaxLength, nullable: false),
                Description = table.Column<string>(type: SqlTypes.NVarChar(AccessLimits.PermissionDescriptionMaxLength), maxLength: AccessLimits.PermissionDescriptionMaxLength, nullable: false),
                IsObsolete = table.Column<bool>(type: SqlTypes.Bit, nullable: false),
            },
            constraints: table => table.PrimaryKey(AccessMigrationIds.PermissionsPrimaryKey, permission => permission.Id));

    private static void CreateRoles(MigrationBuilder migrationBuilder) =>
        migrationBuilder.CreateTable(
            name: AccessTableNames.Roles,
            schema: Schema,
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
            schema: Schema,
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
                    principalSchema: Schema,
                    principalTable: AccessTableNames.Permissions,
                    principalColumn: nameof(PermissionEntry.Id),
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: AccessMigrationIds.RolePermissionsRoleForeignKey,
                    column: granted => granted.RoleId,
                    principalSchema: Schema,
                    principalTable: AccessTableNames.Roles,
                    principalColumn: nameof(Role.Id),
                    onDelete: ReferentialAction.Cascade);
            });

    private static void CreateUserPermissionGrants(MigrationBuilder migrationBuilder) =>
        migrationBuilder.CreateTable(
            name: AccessTableNames.UserPermissionGrants,
            schema: Schema,
            columns: table => new
            {
                Id = table.Column<long>(type: SqlTypes.BigInt, nullable: false)
                    .Annotation(SqlTypes.IdentityAnnotation, SqlTypes.IdentitySeedIncrement),
                UserId = table.Column<long>(type: SqlTypes.BigInt, nullable: false),
                PermissionId = table.Column<long>(type: SqlTypes.BigInt, nullable: false),
                Scope = table.Column<int>(type: SqlTypes.EnumColumn, nullable: false),
                GrantedAt = table.Column<DateTimeOffset>(type: SqlTypes.DateTimeOffset, nullable: false),
            },
            constraints: table =>
            {
                table.PrimaryKey(AccessMigrationIds.UserPermissionGrantsPrimaryKey, grant => grant.Id);
                table.ForeignKey(
                    name: AccessMigrationIds.UserPermissionGrantsPermissionForeignKey,
                    column: grant => grant.PermissionId,
                    principalSchema: Schema,
                    principalTable: AccessTableNames.Permissions,
                    principalColumn: nameof(PermissionEntry.Id),
                    onDelete: ReferentialAction.Restrict);
            });

    private static void CreateUserRoleAssignments(MigrationBuilder migrationBuilder) =>
        migrationBuilder.CreateTable(
            name: AccessTableNames.UserRoleAssignments,
            schema: Schema,
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
                    principalSchema: Schema,
                    principalTable: AccessTableNames.Roles,
                    principalColumn: nameof(Role.Id),
                    onDelete: ReferentialAction.Restrict);
            });

    private static void CreateIndexes(MigrationBuilder migrationBuilder)
    {
        Index(migrationBuilder, AccessMigrationIds.PermissionsKeyIndex, AccessTableNames.Permissions, [nameof(PermissionEntry.Key)], unique: true);
        Index(migrationBuilder, AccessMigrationIds.RolesCodeIndex, AccessTableNames.Roles, [nameof(Role.Code)], unique: true);
        Index(migrationBuilder, AccessMigrationIds.RolesNameArIndex, AccessTableNames.Roles, [nameof(Role.NameAr)], unique: true);
        Index(migrationBuilder, AccessMigrationIds.RolesNameEnIndex, AccessTableNames.Roles, [nameof(Role.NameEn)], unique: true);
        migrationBuilder.CreateIndex(
            name: AccessMigrationIds.RolesVirtualKindIndex,
            schema: Schema,
            table: AccessTableNames.Roles,
            column: nameof(Role.VirtualKind),
            unique: true,
            filter: RoleConfiguration.VirtualKindFilter);
        Index(migrationBuilder, AccessMigrationIds.RolePermissionsPermissionIndex, AccessTableNames.RolePermissions, [nameof(RolePermissionEntry.PermissionId)], unique: false);
        Index(migrationBuilder, AccessMigrationIds.RolePermissionsRolePermissionIndex, AccessTableNames.RolePermissions, RolePermissionColumns, unique: true);
        Index(migrationBuilder, AccessMigrationIds.UserPermissionGrantsPermissionIndex, AccessTableNames.UserPermissionGrants, [nameof(UserPermissionGrant.PermissionId)], unique: false);
        Index(migrationBuilder, AccessMigrationIds.UserPermissionGrantsUserPermissionIndex, AccessTableNames.UserPermissionGrants, UserPermissionColumns, unique: true);
        Index(migrationBuilder, AccessMigrationIds.UserRoleAssignmentsRoleIndex, AccessTableNames.UserRoleAssignments, [nameof(UserRoleAssignment.RoleId)], unique: false);
        Index(migrationBuilder, AccessMigrationIds.UserRoleAssignmentsUserRoleIndex, AccessTableNames.UserRoleAssignments, UserRoleColumns, unique: true);
    }

    private static void Index(MigrationBuilder migrationBuilder, string name, string table, string[] columns, bool unique) =>
        migrationBuilder.CreateIndex(name: name, schema: Schema, table: table, columns: columns, unique: unique);

    private static void SeedVirtualRoles(MigrationBuilder migrationBuilder)
    {
        foreach (var row in VirtualRoleSeed.Rows)
        {
            migrationBuilder.InsertData(
                table: AccessTableNames.Roles,
                schema: Schema,
                columns: SeedColumns,
                values: [row.Code, row.NameEn, row.NameAr, true, (int)row.Kind, VirtualRoleSeed.SeededAt, VirtualRoleSeed.SeededAt]);
        }
    }
}
