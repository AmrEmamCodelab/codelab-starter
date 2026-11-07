using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Operations;
using Microsoft.EntityFrameworkCore.Migrations.Operations.Builders;
using Starter.Modules.Ops.Domain;

namespace Starter.Modules.Ops.Infrastructure.Persistence.Migrations;

/// <summary>
/// The ops schema: the operator audit trail <c>ops.OperatorAccess</c> and the reference-code index
/// <c>ops.ReferenceCodeSightings</c>, with their indexes. Written in the reshaped migration style of the other
/// modules; <see cref="OpsMigrationModel"/> is its target model and the snapshot, and the model test proves the
/// context has no pending change against it. See MODULE.md.
/// </summary>
[DbContext(typeof(OpsDbContext))]
[Migration(OpsMigrationIds.InitialCreate)]
public sealed class InitialCreate : Migration
{
    private static readonly string[] ReferenceCodeIdColumns = [nameof(ReferenceCodeSighting.ReferenceCode), nameof(ReferenceCodeSighting.Id)];

    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        ArgumentNullException.ThrowIfNull(migrationBuilder);
        migrationBuilder.EnsureSchema(name: OpsDbContext.SchemaName);
        CreateOperatorAccess(migrationBuilder);
        CreateReferenceCodeSightings(migrationBuilder);
        migrationBuilder.CreateIndex(
            name: OpsTableNames.OperatorAccessAtIndex,
            schema: OpsDbContext.SchemaName,
            table: OpsTableNames.OperatorAccess,
            column: nameof(OperatorAccess.At));
        migrationBuilder.CreateIndex(
            name: OpsTableNames.ReferenceCodeIdIndex,
            schema: OpsDbContext.SchemaName,
            table: OpsTableNames.ReferenceCodeSightings,
            columns: ReferenceCodeIdColumns);
        migrationBuilder.CreateIndex(
            name: OpsTableNames.OccurredAtIndex,
            schema: OpsDbContext.SchemaName,
            table: OpsTableNames.ReferenceCodeSightings,
            column: nameof(ReferenceCodeSighting.OccurredAt));
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        ArgumentNullException.ThrowIfNull(migrationBuilder);
        migrationBuilder.DropTable(name: OpsTableNames.ReferenceCodeSightings, schema: OpsDbContext.SchemaName);
        migrationBuilder.DropTable(name: OpsTableNames.OperatorAccess, schema: OpsDbContext.SchemaName);
    }

    /// <inheritdoc />
    protected override void BuildTargetModel(ModelBuilder modelBuilder) => OpsMigrationModel.Build(modelBuilder);

    private static void CreateOperatorAccess(MigrationBuilder migrationBuilder) =>
        migrationBuilder.CreateTable(
            name: OpsTableNames.OperatorAccess,
            schema: OpsDbContext.SchemaName,
            columns: table => new
            {
                Id = table.Column<long>(type: OpsSqlTypes.BigInt, nullable: false)
                    .Annotation(OpsSqlTypes.IdentityAnnotation, OpsSqlTypes.IdentitySeedIncrement),
                Operator = Text(table, OpsLimits.OperatorMaxLength, nullable: false),
                CallerService = Text(table, OpsLimits.ServiceNameMaxLength, nullable: false),
                Endpoint = table.Column<int>(type: OpsSqlTypes.Int32Column, nullable: false),
                Subject = Text(table, OpsLimits.SubjectMaxLength, nullable: true),
                CorrelationId = Text(table, OpsLimits.CorrelationIdMaxLength, nullable: true),
                At = table.Column<DateTimeOffset>(type: OpsSqlTypes.DateTimeOffset, nullable: false),
            },
            constraints: table => table.PrimaryKey(OpsMigrationIds.OperatorAccessPrimaryKey, access => access.Id));

    private static void CreateReferenceCodeSightings(MigrationBuilder migrationBuilder) =>
        migrationBuilder.CreateTable(
            name: OpsTableNames.ReferenceCodeSightings,
            schema: OpsDbContext.SchemaName,
            columns: table => new
            {
                Id = table.Column<long>(type: OpsSqlTypes.BigInt, nullable: false)
                    .Annotation(OpsSqlTypes.IdentityAnnotation, OpsSqlTypes.IdentitySeedIncrement),
                ReferenceCode = Text(table, OpsLimits.ReferenceCodeLength, nullable: false),
                CorrelationId = Text(table, OpsLimits.CorrelationIdMaxLength, nullable: false),
                Method = Text(table, OpsLimits.MethodMaxLength, nullable: false),
                Path = Text(table, OpsLimits.PathMaxLength, nullable: false),
                StatusCode = table.Column<int>(type: OpsSqlTypes.Int32Column, nullable: false),
                OccurredAt = table.Column<DateTimeOffset>(type: OpsSqlTypes.DateTimeOffset, nullable: false),
            },
            constraints: table => table.PrimaryKey(OpsMigrationIds.ReferenceCodeSightingsPrimaryKey, sighting => sighting.Id));

    private static OperationBuilder<AddColumnOperation> Text(ColumnsBuilder table, int maxLength, bool nullable) =>
        table.Column<string>(type: OpsSqlTypes.NVarChar(maxLength), maxLength: maxLength, nullable: nullable);
}
