using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace Starter.Infrastructure.Auditing.Migrations;

/// <summary>
/// The audit schema: the data-level audit trail. Written in the shape <c>dotnet ef migrations add</c> (dotnet-ef 10)
/// scaffolds, reshaped to the clean-code rules (no generated marker, named types and lengths, short methods) like the
/// messaging migration; the model-drift test proves it matches <see cref="AuditMigrationModel"/>. The grants (INSERT
/// and SELECT only) come from <c>deploy/sql/audit/</c>.
/// </summary>
[DbContext(typeof(AuditDbContext))]
[Migration(AuditMigrationIds.InitialCreate)]
public sealed class InitialCreate : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        ArgumentNullException.ThrowIfNull(migrationBuilder);
        migrationBuilder.EnsureSchema(name: AuditDbContext.SchemaName);
        CreateAuditEntries(migrationBuilder);
        migrationBuilder.CreateIndex(
            name: AuditMigrationIds.AuditEntriesEntityIndex,
            schema: AuditDbContext.SchemaName,
            table: AuditTableNames.AuditEntries,
            columns: [nameof(AuditEntry.EntityType), nameof(AuditEntry.EntityKey), nameof(AuditEntry.Id)]);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        ArgumentNullException.ThrowIfNull(migrationBuilder);
        migrationBuilder.DropTable(name: AuditTableNames.AuditEntries, schema: AuditDbContext.SchemaName);
    }

    private static void CreateAuditEntries(MigrationBuilder migrationBuilder) =>
        migrationBuilder.CreateTable(
            name: AuditTableNames.AuditEntries,
            schema: AuditDbContext.SchemaName,
            columns: table => new
            {
                Id = table.Column<long>(type: AuditSqlTypes.BigInt, nullable: false)
                    .Annotation(AuditSqlTypes.IdentityAnnotation, AuditSqlTypes.IdentitySeedIncrement),
                EntityType = table.Column<string>(type: AuditSqlTypes.NVarChar(AuditLimits.EntityTypeMaxLength), maxLength: AuditLimits.EntityTypeMaxLength, nullable: false),
                EntityKey = table.Column<string>(type: AuditSqlTypes.NVarChar(AuditLimits.EntityKeyMaxLength), maxLength: AuditLimits.EntityKeyMaxLength, nullable: true),
                Action = table.Column<int>(type: AuditSqlTypes.Int32Column, nullable: false),
                ChangesJson = table.Column<string>(type: AuditSqlTypes.NVarCharMax, nullable: false),
                Actor = table.Column<string>(type: AuditSqlTypes.NVarChar(AuditLimits.ActorMaxLength), maxLength: AuditLimits.ActorMaxLength, nullable: false),
                CorrelationId = table.Column<string>(type: AuditSqlTypes.NVarChar(AuditLimits.CorrelationIdMaxLength), maxLength: AuditLimits.CorrelationIdMaxLength, nullable: true),
                At = table.Column<DateTimeOffset>(type: AuditSqlTypes.DateTimeOffset, nullable: false),
            },
            constraints: table => table.PrimaryKey(AuditMigrationIds.AuditEntriesPrimaryKey, row => row.Id));
}
