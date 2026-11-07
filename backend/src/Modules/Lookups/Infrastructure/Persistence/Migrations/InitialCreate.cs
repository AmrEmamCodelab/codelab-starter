using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Operations;
using Microsoft.EntityFrameworkCore.Migrations.Operations.Builders;
using Starter.Domain.Lookups;
using Starter.Modules.Lookups.Domain;

namespace Starter.Modules.Lookups.Infrastructure.Persistence.Migrations;

/// <summary>
/// The Lookups schema: the lookup types and their values, with the seeded types of <see cref="InitialLookupTypes"/>.
/// Since the v1.0 squash (ADR-010) it is the only Lookups migration and keeps its original id, so a database that
/// applied the old chain stays valid; a later lookup is a new incremental migration written by
/// <c>tools/scaffold/new-lookup.mjs</c>. Written in the reshaped migration style of the other modules;
/// <see cref="LookupsMigrationModel"/> is its target model and the snapshot, and the model test proves the context has
/// no pending change against it. See MODULE.md.
/// </summary>
[DbContext(typeof(LookupsDbContext))]
[Migration(LookupsMigrationIds.InitialCreate)]
public sealed class InitialCreate : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        ArgumentNullException.ThrowIfNull(migrationBuilder);
        migrationBuilder.EnsureSchema(name: LookupsDbContext.SchemaName);
        CreateLookupTypes(migrationBuilder);
        CreateLookupItems(migrationBuilder);
        migrationBuilder.CreateIndex(
            name: LookupsMigrationIds.LookupItemsTypeCodeIndex,
            schema: LookupsDbContext.SchemaName,
            table: LookupsTableNames.LookupItems,
            columns: [nameof(LookupItem.TypeKey), nameof(LookupItem.Code)],
            unique: true);
        foreach (var seed in InitialLookupTypes.All)
        {
            LookupSeed.InsertType(migrationBuilder, seed.TypeKey, seed.Names, seed.Items);
        }
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        ArgumentNullException.ThrowIfNull(migrationBuilder);
        migrationBuilder.DropTable(name: LookupsTableNames.LookupItems, schema: LookupsDbContext.SchemaName);
        migrationBuilder.DropTable(name: LookupsTableNames.LookupTypes, schema: LookupsDbContext.SchemaName);
    }

    /// <inheritdoc />
    protected override void BuildTargetModel(ModelBuilder modelBuilder) => LookupsMigrationModel.Build(modelBuilder);

    private static void CreateLookupTypes(MigrationBuilder migrationBuilder) =>
        migrationBuilder.CreateTable(
            name: LookupsTableNames.LookupTypes,
            schema: LookupsDbContext.SchemaName,
            columns: table => new
            {
                Key = Text(table, LookupLimits.TypeKeyMaxLength),
                NameAr = Text(table, LookupLimits.NameMaxLength),
                NameEn = Text(table, LookupLimits.NameMaxLength),
            },
            constraints: table => table.PrimaryKey(LookupsMigrationIds.LookupTypesPrimaryKey, type => type.Key));

    private static void CreateLookupItems(MigrationBuilder migrationBuilder) =>
        migrationBuilder.CreateTable(
            name: LookupsTableNames.LookupItems,
            schema: LookupsDbContext.SchemaName,
            columns: table => new
            {
                TypeKey = Text(table, LookupLimits.TypeKeyMaxLength),
                Id = table.Column<int>(type: LookupsSqlTypes.Int32Column, nullable: false),
                Code = Text(table, LookupLimits.CodeMaxLength),
                NameAr = Text(table, LookupLimits.NameMaxLength),
                NameEn = Text(table, LookupLimits.NameMaxLength),
                SortOrder = table.Column<int>(type: LookupsSqlTypes.Int32Column, nullable: false),
                IsActive = table.Column<bool>(type: LookupsSqlTypes.Bit, nullable: false),
            },
            constraints: table =>
            {
                table.PrimaryKey(LookupsMigrationIds.LookupItemsPrimaryKey, item => new { item.TypeKey, item.Id });
                table.ForeignKey(
                    name: LookupsMigrationIds.LookupItemsTypeForeignKey,
                    column: item => item.TypeKey,
                    principalSchema: LookupsDbContext.SchemaName,
                    principalTable: LookupsTableNames.LookupTypes,
                    principalColumn: nameof(LookupType.Key),
                    onDelete: ReferentialAction.Restrict);
            });

    private static OperationBuilder<AddColumnOperation> Text(ColumnsBuilder table, int maxLength) =>
        table.Column<string>(type: LookupsSqlTypes.NVarChar(maxLength), maxLength: maxLength, nullable: false);
}
