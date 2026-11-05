using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Starter.Modules.Access.Domain;
using Starter.Modules.Access.Infrastructure.Persistence.Configurations;

namespace Starter.Modules.Access.Infrastructure.Persistence.Migrations;

/// <summary>
/// The Access schema: the permission catalog and the direct user grants. Scaffolded with <c>dotnet ef migrations add</c>
/// and reshaped to the clean-code rules (no generated marker, named lengths, short methods); see MODULE.md.
/// </summary>
[DbContext(typeof(AccessDbContext))]
[Migration(AccessMigrationIds.InitialCreate)]
public sealed class InitialCreate : Migration
{
    private static readonly string[] UserPermissionColumns = [nameof(UserPermissionGrant.UserId), nameof(UserPermissionGrant.PermissionId)];

    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        ArgumentNullException.ThrowIfNull(migrationBuilder);
        migrationBuilder.EnsureSchema(name: AccessDbContext.SchemaName);
        CreatePermissions(migrationBuilder);
        CreateUserPermissionGrants(migrationBuilder);
        CreateIndexes(migrationBuilder);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        ArgumentNullException.ThrowIfNull(migrationBuilder);
        migrationBuilder.DropTable(name: AccessTableNames.UserPermissionGrants, schema: AccessDbContext.SchemaName);
        migrationBuilder.DropTable(name: AccessTableNames.Permissions, schema: AccessDbContext.SchemaName);
    }

    /// <inheritdoc />
    protected override void BuildTargetModel(ModelBuilder modelBuilder) => AccessMigrationModel.Build(modelBuilder);

    private static void CreatePermissions(MigrationBuilder migrationBuilder) =>
        migrationBuilder.CreateTable(
            name: AccessTableNames.Permissions,
            schema: AccessDbContext.SchemaName,
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

    private static void CreateUserPermissionGrants(MigrationBuilder migrationBuilder) =>
        migrationBuilder.CreateTable(
            name: AccessTableNames.UserPermissionGrants,
            schema: AccessDbContext.SchemaName,
            columns: table => new
            {
                Id = table.Column<long>(type: SqlTypes.BigInt, nullable: false)
                    .Annotation(SqlTypes.IdentityAnnotation, SqlTypes.IdentitySeedIncrement),
                UserId = table.Column<long>(type: SqlTypes.BigInt, nullable: false),
                PermissionId = table.Column<long>(type: SqlTypes.BigInt, nullable: false),
                GrantedAt = table.Column<DateTimeOffset>(type: SqlTypes.DateTimeOffset, nullable: false),
            },
            constraints: table =>
            {
                table.PrimaryKey(AccessMigrationIds.UserPermissionGrantsPrimaryKey, grant => grant.Id);
                table.ForeignKey(
                    name: AccessMigrationIds.UserPermissionGrantsPermissionForeignKey,
                    column: grant => grant.PermissionId,
                    principalSchema: AccessDbContext.SchemaName,
                    principalTable: AccessTableNames.Permissions,
                    principalColumn: nameof(PermissionEntry.Id),
                    onDelete: ReferentialAction.Restrict);
            });

    private static void CreateIndexes(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateIndex(
            name: AccessMigrationIds.PermissionsKeyIndex,
            schema: AccessDbContext.SchemaName,
            table: AccessTableNames.Permissions,
            column: nameof(PermissionEntry.Key),
            unique: true);
        migrationBuilder.CreateIndex(
            name: AccessMigrationIds.UserPermissionGrantsPermissionIndex,
            schema: AccessDbContext.SchemaName,
            table: AccessTableNames.UserPermissionGrants,
            column: nameof(UserPermissionGrant.PermissionId));
        migrationBuilder.CreateIndex(
            name: AccessMigrationIds.UserPermissionGrantsUserPermissionIndex,
            schema: AccessDbContext.SchemaName,
            table: AccessTableNames.UserPermissionGrants,
            columns: UserPermissionColumns,
            unique: true);
    }
}
