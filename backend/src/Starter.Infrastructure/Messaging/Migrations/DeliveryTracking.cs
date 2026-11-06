using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace Starter.Infrastructure.Messaging.Migrations;

/// <summary>
/// The drain's columns on <c>messaging.OutboxMessages</c>: <c>Attempt</c> (existing rows start at 0), the lease
/// <c>LockedUntil</c>, <c>SentAt</c> and the classified <c>LastError</c>. Written in the shape
/// <c>dotnet ef migrations add</c> scaffolds and reshaped like <see cref="InitialCreate"/>; the model-drift test proves
/// it matches <see cref="OutboxMigrationModel"/>.
/// </summary>
[DbContext(typeof(OutboxDbContext))]
[Migration(OutboxMigrationIds.DeliveryTracking)]
public sealed class DeliveryTracking : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        ArgumentNullException.ThrowIfNull(migrationBuilder);
        migrationBuilder.AddColumn<int>(
            name: nameof(OutboxEntry.Attempt),
            schema: OutboxDbContext.SchemaName,
            table: OutboxTableNames.OutboxMessages,
            type: OutboxSqlTypes.Int32Column,
            nullable: false,
            defaultValue: 0);
        AddTimeColumn(migrationBuilder, nameof(OutboxEntry.LockedUntil));
        AddTimeColumn(migrationBuilder, nameof(OutboxEntry.SentAt));
        migrationBuilder.AddColumn<string>(
            name: nameof(OutboxEntry.LastError),
            schema: OutboxDbContext.SchemaName,
            table: OutboxTableNames.OutboxMessages,
            type: OutboxSqlTypes.NVarChar(OutboxColumnLimits.LastErrorMaxLength),
            maxLength: OutboxColumnLimits.LastErrorMaxLength,
            nullable: true);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        ArgumentNullException.ThrowIfNull(migrationBuilder);
        foreach (var column in new[] { nameof(OutboxEntry.LastError), nameof(OutboxEntry.SentAt), nameof(OutboxEntry.LockedUntil), nameof(OutboxEntry.Attempt) })
        {
            migrationBuilder.DropColumn(name: column, schema: OutboxDbContext.SchemaName, table: OutboxTableNames.OutboxMessages);
        }
    }

    /// <inheritdoc />
    protected override void BuildTargetModel(ModelBuilder modelBuilder) => OutboxMigrationModel.Build(modelBuilder);

    private static void AddTimeColumn(MigrationBuilder migrationBuilder, string name) =>
        migrationBuilder.AddColumn<DateTimeOffset>(
            name: name,
            schema: OutboxDbContext.SchemaName,
            table: OutboxTableNames.OutboxMessages,
            type: OutboxSqlTypes.DateTimeOffset,
            nullable: true);
}
