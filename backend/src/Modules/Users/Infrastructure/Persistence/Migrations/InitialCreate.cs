using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Operations;
using Microsoft.EntityFrameworkCore.Migrations.Operations.Builders;
using Starter.Modules.Users.Domain;

namespace Starter.Modules.Users.Infrastructure.Persistence.Migrations;

/// <summary>
/// The Users schema: organizations and import batches. Written in the reshaped migration style of the other modules
/// (no generated marker, named lengths and types, short methods); <see cref="UsersMigrationModel"/> is its target model
/// and the snapshot, and the model test proves the context has no pending change against it. See MODULE.md.
/// </summary>
[DbContext(typeof(UsersDbContext))]
[Migration(UsersMigrationIds.InitialCreate)]
public sealed class InitialCreate : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        ArgumentNullException.ThrowIfNull(migrationBuilder);
        migrationBuilder.EnsureSchema(name: UsersDbContext.SchemaName);
        CreateOrganizations(migrationBuilder);
        CreateImportBatches(migrationBuilder);
        migrationBuilder.CreateIndex(
            name: UsersMigrationIds.OrganizationsCodeIndex,
            schema: UsersDbContext.SchemaName,
            table: UsersTableNames.Organizations,
            column: nameof(Organization.Code),
            unique: true);
        migrationBuilder.CreateIndex(
            name: UsersMigrationIds.ImportBatchesExpiresAtIndex,
            schema: UsersDbContext.SchemaName,
            table: UsersTableNames.ImportBatches,
            column: nameof(ImportBatch.ExpiresAt));
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        ArgumentNullException.ThrowIfNull(migrationBuilder);
        migrationBuilder.DropTable(name: UsersTableNames.ImportBatches, schema: UsersDbContext.SchemaName);
        migrationBuilder.DropTable(name: UsersTableNames.Organizations, schema: UsersDbContext.SchemaName);
    }

    /// <inheritdoc />
    protected override void BuildTargetModel(ModelBuilder modelBuilder) => UsersMigrationModel.Build(modelBuilder);

    private static void CreateOrganizations(MigrationBuilder migrationBuilder) =>
        migrationBuilder.CreateTable(
            name: UsersTableNames.Organizations,
            schema: UsersDbContext.SchemaName,
            columns: table => new
            {
                Id = IdentityKey(table),
                Code = Text(table, UsersLimits.OrganizationCodeMaxLength),
                NameEn = Text(table, UsersLimits.OrganizationNameMaxLength),
                NameAr = Text(table, UsersLimits.OrganizationNameMaxLength),
                Kind = table.Column<int>(type: UsersSqlTypes.Int32Column, nullable: false),
                IsActive = table.Column<bool>(type: UsersSqlTypes.Bit, nullable: false),
                CreatedAt = table.Column<DateTimeOffset>(type: UsersSqlTypes.DateTimeOffset, nullable: false),
                UpdatedAt = table.Column<DateTimeOffset>(type: UsersSqlTypes.DateTimeOffset, nullable: true),
            },
            constraints: table => table.PrimaryKey(UsersMigrationIds.OrganizationsPrimaryKey, organization => organization.Id));

    private static void CreateImportBatches(MigrationBuilder migrationBuilder) =>
        migrationBuilder.CreateTable(
            name: UsersTableNames.ImportBatches,
            schema: UsersDbContext.SchemaName,
            columns: table => new
            {
                Id = IdentityKey(table),
                Status = table.Column<int>(type: UsersSqlTypes.Int32Column, nullable: false),
                RowsJson = table.Column<string>(type: UsersSqlTypes.NVarCharMax, nullable: false),
                RowCount = table.Column<int>(type: UsersSqlTypes.Int32Column, nullable: false),
                ValidRowCount = table.Column<int>(type: UsersSqlTypes.Int32Column, nullable: false),
                CreatedBySubjectId = table.Column<Guid>(type: UsersSqlTypes.UniqueIdentifier, nullable: false),
                CreatedAt = table.Column<DateTimeOffset>(type: UsersSqlTypes.DateTimeOffset, nullable: false),
                ExpiresAt = table.Column<DateTimeOffset>(type: UsersSqlTypes.DateTimeOffset, nullable: false),
                CommittedAt = table.Column<DateTimeOffset>(type: UsersSqlTypes.DateTimeOffset, nullable: true),
            },
            constraints: table => table.PrimaryKey(UsersMigrationIds.ImportBatchesPrimaryKey, batch => batch.Id));

    private static OperationBuilder<AddColumnOperation> IdentityKey(ColumnsBuilder table) =>
        table.Column<long>(type: UsersSqlTypes.BigInt, nullable: false)
            .Annotation(UsersSqlTypes.IdentityAnnotation, UsersSqlTypes.IdentitySeedIncrement);

    private static OperationBuilder<AddColumnOperation> Text(ColumnsBuilder table, int maxLength) =>
        table.Column<string>(type: UsersSqlTypes.NVarChar(maxLength), maxLength: maxLength, nullable: false);
}
