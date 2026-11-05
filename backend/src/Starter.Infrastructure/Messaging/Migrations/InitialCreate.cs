using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Starter.Application.Messaging;

namespace Starter.Infrastructure.Messaging.Migrations;

/// <summary>
/// The messaging schema: the outbox table. Written in the shape <c>dotnet ef migrations add</c> (dotnet-ef 10)
/// scaffolds, reshaped to the clean-code rules (no generated marker, named types and lengths, short methods) like the
/// module migrations; the model-drift test proves it matches <see cref="OutboxMigrationModel"/>.
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
            },
            constraints: table => table.PrimaryKey(OutboxMigrationIds.OutboxMessagesPrimaryKey, row => row.Id));
}
