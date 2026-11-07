using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Starter.Application.Messaging;

namespace Starter.Infrastructure.Messaging.Migrations;

/// <summary>
/// The whole messaging schema in one migration (ADR-010 squash): the outbox table with the drain's columns
/// (<c>Attempt</c>, the lease <c>LockedUntil</c>, <c>SentAt</c> and the classified <c>LastError</c>). It keeps the id of
/// the first migration so a database migrated before the squash stays valid. Written in the shape
/// <c>dotnet ef migrations add</c> (dotnet-ef 10) scaffolds, reshaped to the clean-code rules (no generated marker,
/// named types and lengths, short methods); the model-drift test proves it matches <see cref="OutboxMigrationModel"/>.
/// </summary>
[DbContext(typeof(OutboxDbContext))]
[Migration(OutboxMigrationIds.InitialCreate)]
public sealed class InitialCreate : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        ArgumentNullException.ThrowIfNull(migrationBuilder);
        migrationBuilder.EnsureSchema(name: OutboxDbContext.SchemaName);
        CreateOutboxMessages(migrationBuilder);
        migrationBuilder.CreateIndex(
            name: OutboxMigrationIds.OutboxMessagesStatusIndex,
            schema: OutboxDbContext.SchemaName,
            table: OutboxTableNames.OutboxMessages,
            columns: [nameof(OutboxEntry.Status), nameof(OutboxEntry.CreatedAt)]);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        ArgumentNullException.ThrowIfNull(migrationBuilder);
        migrationBuilder.DropTable(name: OutboxTableNames.OutboxMessages, schema: OutboxDbContext.SchemaName);
    }

    /// <inheritdoc />
    protected override void BuildTargetModel(ModelBuilder modelBuilder) => OutboxMigrationModel.Build(modelBuilder);

    private static void CreateOutboxMessages(MigrationBuilder migrationBuilder) =>
        migrationBuilder.CreateTable(
            name: OutboxTableNames.OutboxMessages,
            schema: OutboxDbContext.SchemaName,
            columns: table => new
            {
                Id = table.Column<long>(type: OutboxSqlTypes.BigInt, nullable: false)
                    .Annotation(OutboxSqlTypes.IdentityAnnotation, OutboxSqlTypes.IdentitySeedIncrement),
                Type = table.Column<string>(type: OutboxSqlTypes.NVarChar(OutboxLimits.TypeMaxLength), maxLength: OutboxLimits.TypeMaxLength, nullable: false),
                PayloadJson = table.Column<string>(type: OutboxSqlTypes.NVarCharMax, nullable: false),
                Status = table.Column<int>(type: OutboxSqlTypes.Int32Column, nullable: false),
                CreatedAt = table.Column<DateTimeOffset>(type: OutboxSqlTypes.DateTimeOffset, nullable: false),
                NotBefore = table.Column<DateTimeOffset>(type: OutboxSqlTypes.DateTimeOffset, nullable: true),
                Attempt = table.Column<int>(type: OutboxSqlTypes.Int32Column, nullable: false),
                LockedUntil = table.Column<DateTimeOffset>(type: OutboxSqlTypes.DateTimeOffset, nullable: true),
                SentAt = table.Column<DateTimeOffset>(type: OutboxSqlTypes.DateTimeOffset, nullable: true),
                LastError = table.Column<string>(
                    type: OutboxSqlTypes.NVarChar(OutboxColumnLimits.LastErrorMaxLength), maxLength: OutboxColumnLimits.LastErrorMaxLength, nullable: true),
            },
            constraints: table => table.PrimaryKey(OutboxMigrationIds.OutboxMessagesPrimaryKey, row => row.Id));
}
