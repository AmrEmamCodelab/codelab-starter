using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Operations;
using Microsoft.EntityFrameworkCore.Migrations.Operations.Builders;
using Starter.Modules.Jobs.Domain;

namespace Starter.Modules.Jobs.Infrastructure.Persistence.Migrations;

/// <summary>
/// The jobs schema: the run history table <c>jobs.JobRuns</c> and its (JobKey, Id) index. The scheduler's own tables
/// (schema <c>hangfire</c>) are not EF's: the scheduler installs them (<c>Jobs:PrepareSchemaIfNecessary</c>) or the DBA
/// does. Written in the reshaped migration style of the other modules; <see cref="JobsMigrationModel"/> is its target
/// model and the snapshot, and the model test proves the context has no pending change against it. See MODULE.md.
/// </summary>
[DbContext(typeof(JobsDbContext))]
[Migration(JobsMigrationIds.InitialCreate)]
public sealed class InitialCreate : Migration
{
    private static readonly string[] JobKeyIdColumns = [nameof(JobRun.JobKey), nameof(JobRun.Id)];

    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        ArgumentNullException.ThrowIfNull(migrationBuilder);
        migrationBuilder.EnsureSchema(name: JobsDbContext.SchemaName);
        CreateJobRuns(migrationBuilder);
        migrationBuilder.CreateIndex(
            name: JobsTableNames.JobKeyIdIndex,
            schema: JobsDbContext.SchemaName,
            table: JobsTableNames.JobRuns,
            columns: JobKeyIdColumns);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        ArgumentNullException.ThrowIfNull(migrationBuilder);
        migrationBuilder.DropTable(name: JobsTableNames.JobRuns, schema: JobsDbContext.SchemaName);
    }

    /// <inheritdoc />
    protected override void BuildTargetModel(ModelBuilder modelBuilder) => JobsMigrationModel.Build(modelBuilder);

    private static void CreateJobRuns(MigrationBuilder migrationBuilder) =>
        migrationBuilder.CreateTable(
            name: JobsTableNames.JobRuns,
            schema: JobsDbContext.SchemaName,
            columns: table => new
            {
                Id = table.Column<long>(type: JobsSqlTypes.BigInt, nullable: false)
                    .Annotation(JobsSqlTypes.IdentityAnnotation, JobsSqlTypes.IdentitySeedIncrement),
                JobKey = Text(table, JobsLimits.JobKeyMaxLength, nullable: false),
                Status = table.Column<int>(type: JobsSqlTypes.Int32Column, nullable: false),
                Trigger = table.Column<int>(type: JobsSqlTypes.Int32Column, nullable: false),
                Actor = Text(table, JobsLimits.ActorMaxLength, nullable: false),
                TriggeredBySubjectId = table.Column<Guid>(type: JobsSqlTypes.UniqueIdentifier, nullable: true),
                QueuedAt = table.Column<DateTimeOffset>(type: JobsSqlTypes.DateTimeOffset, nullable: false),
                StartedAt = table.Column<DateTimeOffset>(type: JobsSqlTypes.DateTimeOffset, nullable: true),
                FinishedAt = table.Column<DateTimeOffset>(type: JobsSqlTypes.DateTimeOffset, nullable: true),
                CorrelationId = Text(table, JobsLimits.CorrelationIdMaxLength, nullable: true),
                ErrorCode = Text(table, JobsLimits.ErrorCodeMaxLength, nullable: true),
                ReferenceCode = Text(table, JobsLimits.ReferenceCodeMaxLength, nullable: true),
            },
            constraints: table => table.PrimaryKey(JobsMigrationIds.JobRunsPrimaryKey, run => run.Id));

    private static OperationBuilder<AddColumnOperation> Text(ColumnsBuilder table, int maxLength, bool nullable) =>
        table.Column<string>(type: JobsSqlTypes.NVarChar(maxLength), maxLength: maxLength, nullable: nullable);
}
