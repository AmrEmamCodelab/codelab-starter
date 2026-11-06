using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Starter.Modules.Deadlines.Domain;

namespace Starter.Modules.Deadlines.Infrastructure.Persistence.Migrations;

/// <summary>
/// The deadlines schema: <c>deadlines.RecordDeadlines</c> with its two indexes over the open deadlines (one open deadline
/// per record; open deadlines by due time). Written in the reshaped migration style of the other modules;
/// <see cref="DeadlinesMigrationModel"/> is its target model and the snapshot, and the model test proves the context has
/// no pending change against it. See MODULE.md.
/// </summary>
[DbContext(typeof(DeadlinesDbContext))]
[Migration(DeadlinesMigrationIds.InitialCreate)]
public sealed class InitialCreate : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        ArgumentNullException.ThrowIfNull(migrationBuilder);
        migrationBuilder.EnsureSchema(name: DeadlinesDbContext.SchemaName);
        CreateRecordDeadlines(migrationBuilder);
        migrationBuilder.CreateIndex(
            name: DeadlinesTableNames.OneOpenPerRecordIndex,
            schema: DeadlinesDbContext.SchemaName,
            table: DeadlinesTableNames.RecordDeadlines,
            column: nameof(RecordDeadline.RecordId),
            unique: true,
            filter: DeadlinesTableNames.OpenFilter);
        migrationBuilder.CreateIndex(
            name: DeadlinesTableNames.OpenByDueAtIndex,
            schema: DeadlinesDbContext.SchemaName,
            table: DeadlinesTableNames.RecordDeadlines,
            column: nameof(RecordDeadline.DueAt),
            filter: DeadlinesTableNames.OpenFilter);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        ArgumentNullException.ThrowIfNull(migrationBuilder);
        migrationBuilder.DropTable(name: DeadlinesTableNames.RecordDeadlines, schema: DeadlinesDbContext.SchemaName);
    }

    /// <inheritdoc />
    protected override void BuildTargetModel(ModelBuilder modelBuilder) => DeadlinesMigrationModel.Build(modelBuilder);

    private static void CreateRecordDeadlines(MigrationBuilder migrationBuilder) =>
        migrationBuilder.CreateTable(
            name: DeadlinesTableNames.RecordDeadlines,
            schema: DeadlinesDbContext.SchemaName,
            columns: table => new
            {
                Id = table.Column<long>(type: DeadlinesSqlTypes.BigInt, nullable: false)
                    .Annotation(DeadlinesSqlTypes.IdentityAnnotation, DeadlinesSqlTypes.IdentitySeedIncrement),
                RecordId = table.Column<long>(type: DeadlinesSqlTypes.BigInt, nullable: false),
                RecordType = table.Column<string>(
                    type: DeadlinesSqlTypes.NVarChar(DeadlinesLimits.RecordTypeMaxLength),
                    maxLength: DeadlinesLimits.RecordTypeMaxLength,
                    nullable: false),
                Status = table.Column<int>(type: DeadlinesSqlTypes.Int32Column, nullable: false),
                StartedAt = table.Column<DateTimeOffset>(type: DeadlinesSqlTypes.DateTimeOffset, nullable: false),
                DueAt = table.Column<DateTimeOffset>(type: DeadlinesSqlTypes.DateTimeOffset, nullable: false),
                DueSoonAt = table.Column<DateTimeOffset>(type: DeadlinesSqlTypes.DateTimeOffset, nullable: false),
                BreachedAt = table.Column<DateTimeOffset>(type: DeadlinesSqlTypes.DateTimeOffset, nullable: true),
                EscalatedAt = table.Column<DateTimeOffset>(type: DeadlinesSqlTypes.DateTimeOffset, nullable: true),
                ClosedAt = table.Column<DateTimeOffset>(type: DeadlinesSqlTypes.DateTimeOffset, nullable: true),
            },
            constraints: table => table.PrimaryKey(DeadlinesMigrationIds.RecordDeadlinesPrimaryKey, deadline => deadline.Id));
}
