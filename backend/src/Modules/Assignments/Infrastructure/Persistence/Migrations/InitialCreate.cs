using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Starter.Modules.Assignments.Domain;

namespace Starter.Modules.Assignments.Infrastructure.Persistence.Migrations;

/// <summary>
/// The assignments schema: the row history table <c>assignments.RecordAssignments</c>, the filtered unique index that
/// allows one open row per record, the open-by-assignee index and the history index. Written in the reshaped migration
/// style of the other modules; <see cref="AssignmentsMigrationModel"/> is its target model and the snapshot, and the
/// model test proves the context has no pending change against it. See MODULE.md.
/// </summary>
[DbContext(typeof(AssignmentsDbContext))]
[Migration(AssignmentsMigrationIds.InitialCreate)]
public sealed class InitialCreate : Migration
{
    private static readonly string[] OpenByAssigneeColumns = [nameof(RecordAssignment.RecordType), nameof(RecordAssignment.AssigneeUserId)];
    private static readonly string[] HistoryColumns = [nameof(RecordAssignment.RecordId), nameof(RecordAssignment.AssignedAt)];

    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        ArgumentNullException.ThrowIfNull(migrationBuilder);
        migrationBuilder.EnsureSchema(name: AssignmentsDbContext.SchemaName);
        CreateRecordAssignments(migrationBuilder);
        migrationBuilder.CreateIndex(
            name: AssignmentsTableNames.OneOpenRowPerRecordIndex,
            schema: AssignmentsDbContext.SchemaName,
            table: AssignmentsTableNames.RecordAssignments,
            column: nameof(RecordAssignment.RecordId),
            unique: true,
            filter: AssignmentsTableNames.OpenRowFilter);
        migrationBuilder.CreateIndex(
            name: AssignmentsTableNames.OpenByAssigneeIndex,
            schema: AssignmentsDbContext.SchemaName,
            table: AssignmentsTableNames.RecordAssignments,
            columns: OpenByAssigneeColumns,
            filter: AssignmentsTableNames.OpenRowFilter);
        migrationBuilder.CreateIndex(
            name: AssignmentsTableNames.HistoryIndex,
            schema: AssignmentsDbContext.SchemaName,
            table: AssignmentsTableNames.RecordAssignments,
            columns: HistoryColumns);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        ArgumentNullException.ThrowIfNull(migrationBuilder);
        migrationBuilder.DropTable(name: AssignmentsTableNames.RecordAssignments, schema: AssignmentsDbContext.SchemaName);
    }

    /// <inheritdoc />
    protected override void BuildTargetModel(ModelBuilder modelBuilder) => AssignmentsMigrationModel.Build(modelBuilder);

    private static void CreateRecordAssignments(MigrationBuilder migrationBuilder) =>
        migrationBuilder.CreateTable(
            name: AssignmentsTableNames.RecordAssignments,
            schema: AssignmentsDbContext.SchemaName,
            columns: table => new
            {
                Id = table.Column<long>(type: AssignmentsSqlTypes.BigInt, nullable: false)
                    .Annotation(AssignmentsSqlTypes.IdentityAnnotation, AssignmentsSqlTypes.IdentitySeedIncrement),
                RecordType = table.Column<string>(
                    type: AssignmentsSqlTypes.NVarChar(AssignmentsLimits.RecordTypeMaxLength),
                    maxLength: AssignmentsLimits.RecordTypeMaxLength,
                    nullable: false),
                RecordId = table.Column<long>(type: AssignmentsSqlTypes.BigInt, nullable: false),
                AssigneeUserId = table.Column<long>(type: AssignmentsSqlTypes.BigInt, nullable: false),
                AssignedByUserId = table.Column<long>(type: AssignmentsSqlTypes.BigInt, nullable: false),
                AssignedAt = table.Column<DateTimeOffset>(type: AssignmentsSqlTypes.DateTimeOffset, nullable: false),
                ReleasedAt = table.Column<DateTimeOffset>(type: AssignmentsSqlTypes.DateTimeOffset, nullable: true),
                ReleaseReason = table.Column<int>(type: AssignmentsSqlTypes.Int32Column, nullable: true),
                ReleasedByUserId = table.Column<long>(type: AssignmentsSqlTypes.BigInt, nullable: true),
            },
            constraints: table => table.PrimaryKey(AssignmentsMigrationIds.RecordAssignmentsPrimaryKey, row => row.Id));
}
