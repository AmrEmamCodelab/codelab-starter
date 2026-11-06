using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Operations;
using Microsoft.EntityFrameworkCore.Migrations.Operations.Builders;
using Starter.Domain.Auditing;
using Starter.Modules.Records.Domain;

namespace Starter.Modules.Records.Infrastructure.Persistence.Migrations;

/// <summary>
/// The Records schema: the record base rows (identity id, record type, filtered unique number, int status, owner,
/// organization, times, rowversion) and their timeline (RecordHistory, ON DELETE RESTRICT to the record). Written in
/// the reshaped migration style of the other modules; <see cref="RecordsMigrationModel"/> is its target model and the
/// snapshot, and the model test proves the context has no pending change against it. No seed data.
/// </summary>
[DbContext(typeof(RecordsDbContext))]
[Migration(RecordsMigrationIds.InitialCreate)]
public sealed class InitialCreate : Migration
{
    private static readonly string[] OwnerIndexColumns = [nameof(Record.RecordType), nameof(Record.OwnerUserId)];

    private static readonly string[] StatusIndexColumns = [nameof(Record.RecordType), nameof(Record.Status)];

    private static readonly string[] TimelineIndexColumns = [nameof(RecordHistoryEntry.RecordId), nameof(RecordHistoryEntry.At)];

    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        ArgumentNullException.ThrowIfNull(migrationBuilder);
        migrationBuilder.EnsureSchema(name: RecordsDbContext.SchemaName);
        CreateRecords(migrationBuilder);
        CreateHistory(migrationBuilder);
        migrationBuilder.CreateIndex(
            name: RecordsMigrationIds.NumberIndex,
            schema: RecordsDbContext.SchemaName,
            table: RecordsTableNames.Records,
            column: nameof(Record.Number),
            unique: true,
            filter: RecordConfiguration.NumberIndexFilter);
        CreateIndex(migrationBuilder, RecordsMigrationIds.OwnerIndex, RecordsTableNames.Records, OwnerIndexColumns);
        CreateIndex(migrationBuilder, RecordsMigrationIds.StatusIndex, RecordsTableNames.Records, StatusIndexColumns);
        CreateIndex(migrationBuilder, RecordsMigrationIds.HistoryTimelineIndex, RecordsTableNames.RecordHistory, TimelineIndexColumns);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        ArgumentNullException.ThrowIfNull(migrationBuilder);
        migrationBuilder.DropTable(name: RecordsTableNames.RecordHistory, schema: RecordsDbContext.SchemaName);
        migrationBuilder.DropTable(name: RecordsTableNames.Records, schema: RecordsDbContext.SchemaName);
    }

    /// <inheritdoc />
    protected override void BuildTargetModel(ModelBuilder modelBuilder) => RecordsMigrationModel.Build(modelBuilder);

    private static void CreateRecords(MigrationBuilder migrationBuilder) =>
        migrationBuilder.CreateTable(
            name: RecordsTableNames.Records,
            schema: RecordsDbContext.SchemaName,
            columns: table => new
            {
                Id = Identity(table),
                RecordType = Text(table, RecordColumnLimits.RecordTypeMaxLength, nullable: false),
                Number = Text(table, RecordColumnLimits.NumberMaxLength, nullable: true),
                Status = table.Column<int>(type: RecordsSqlTypes.Int32Column, nullable: false),
                OwnerUserId = table.Column<long>(type: RecordsSqlTypes.BigInt, nullable: false),
                OrganizationId = table.Column<long>(type: RecordsSqlTypes.BigInt, nullable: true),
                CreatedAt = table.Column<DateTimeOffset>(type: RecordsSqlTypes.DateTimeOffset, nullable: false),
                SubmittedAt = table.Column<DateTimeOffset>(type: RecordsSqlTypes.DateTimeOffset, nullable: true),
                UpdatedAt = table.Column<DateTimeOffset>(type: RecordsSqlTypes.DateTimeOffset, nullable: false),
                RowVersion = table.Column<byte[]>(type: RecordsSqlTypes.RowVersion, rowVersion: true, nullable: false),
            },
            constraints: table => table.PrimaryKey(RecordsMigrationIds.RecordsPrimaryKey, record => record.Id));

    private static void CreateHistory(MigrationBuilder migrationBuilder) =>
        migrationBuilder.CreateTable(
            name: RecordsTableNames.RecordHistory,
            schema: RecordsDbContext.SchemaName,
            columns: table => new
            {
                Id = Identity(table),
                RecordId = table.Column<long>(type: RecordsSqlTypes.BigInt, nullable: false),
                Action = Text(table, RecordHistoryLimits.ActionMaxLength, nullable: false),
                FromStatus = table.Column<int>(type: RecordsSqlTypes.Int32Column, nullable: true),
                ToStatus = table.Column<int>(type: RecordsSqlTypes.Int32Column, nullable: true),
                Reason = Text(table, RecordHistoryLimits.ReasonMaxLength, nullable: true),
                ActorUserId = table.Column<long>(type: RecordsSqlTypes.BigInt, nullable: true),
                ActorName = Text(table, RecordHistoryLimits.ActorNameMaxLength, nullable: false),
                At = table.Column<DateTimeOffset>(type: RecordsSqlTypes.DateTimeOffset, nullable: false),
                Side = table.Column<int>(type: RecordsSqlTypes.Int32Column, nullable: false),
            },
            constraints: table =>
            {
                table.PrimaryKey(RecordsMigrationIds.HistoryPrimaryKey, entry => entry.Id);
                table.ForeignKey(
                    name: RecordsMigrationIds.HistoryRecordForeignKey,
                    column: entry => entry.RecordId,
                    principalSchema: RecordsDbContext.SchemaName,
                    principalTable: RecordsTableNames.Records,
                    principalColumn: nameof(Record.Id),
                    onDelete: ReferentialAction.Restrict);
            });

    private static void CreateIndex(MigrationBuilder migrationBuilder, string name, string table, string[] columns) =>
        migrationBuilder.CreateIndex(name: name, schema: RecordsDbContext.SchemaName, table: table, columns: columns);

    private static OperationBuilder<AddColumnOperation> Identity(ColumnsBuilder table) =>
        table.Column<long>(type: RecordsSqlTypes.BigInt, nullable: false)
            .Annotation(RecordsSqlTypes.IdentityAnnotation, RecordsSqlTypes.IdentitySeedIncrement);

    private static OperationBuilder<AddColumnOperation> Text(ColumnsBuilder table, int maxLength, bool nullable) =>
        table.Column<string>(type: RecordsSqlTypes.NVarChar(maxLength), maxLength: maxLength, nullable: nullable);
}
