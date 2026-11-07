using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Starter.Application.Messaging;

namespace Starter.Infrastructure.Messaging.Migrations;

/// <summary>The migration identifiers and constraint names of the messaging schema.</summary>
public static class OutboxMigrationIds
{
    /// <summary>
    /// The id of the one messaging migration. The v1.0 squash (ADR-010) kept the id of the first migration, so a
    /// database migrated before the squash stays valid.
    /// </summary>
    public const string InitialCreate = "20261105090000_InitialCreate";

    /// <summary>The primary key of <c>messaging.OutboxMessages</c>.</summary>
    public const string OutboxMessagesPrimaryKey = "PK_OutboxMessages";

    /// <summary>The index the drain reads Pending rows in creation order through.</summary>
    public const string OutboxMessagesStatusIndex = "IX_OutboxMessages_Status_CreatedAt";
}

/// <summary>The SQL Server store types and annotations the messaging migrations write.</summary>
public static class OutboxSqlTypes
{
    /// <summary>32-bit integer.</summary>
    public const string Int32Column = "int";

    /// <summary>64-bit integer.</summary>
    public const string BigInt = "bigint";

    /// <summary>Date, time and offset.</summary>
    public const string DateTimeOffset = "datetimeoffset";

    /// <summary>Unbounded Unicode text.</summary>
    public const string NVarCharMax = "nvarchar(max)";

    /// <summary>The identity column annotation.</summary>
    public const string IdentityAnnotation = "SqlServer:Identity";

    /// <summary>Identity seed 1, increment 1.</summary>
    public const string IdentitySeedIncrement = "1, 1";

    /// <summary>The model annotation that records the EF Core version.</summary>
    public const string ProductVersionAnnotation = "ProductVersion";

    /// <summary>The EF Core version that scaffolded the model.</summary>
    public const string ProductVersion = "10.0.14";

    /// <summary>The longest SQL Server identifier.</summary>
    public const int MaxIdentifierLength = 128;

    /// <summary>A Unicode string column of <paramref name="length"/> characters.</summary>
    public static string NVarChar(int length) => $"nvarchar({length})";
}

/// <summary>
/// The messaging model as of the latest migration: the target model of <see cref="InitialCreate"/> and the model
/// snapshot that <c>dotnet ef migrations add</c> diffs against. A later migration first freezes a copy of this model as
/// the target model of <see cref="InitialCreate"/> (<c>InitialCreateModel</c>), then changes this one.
/// </summary>
public static class OutboxMigrationModel
{
    /// <summary>Builds the model.</summary>
    public static void Build(ModelBuilder modelBuilder)
    {
        ArgumentNullException.ThrowIfNull(modelBuilder);
        modelBuilder
            .HasDefaultSchema(OutboxDbContext.SchemaName)
            .HasAnnotation(OutboxSqlTypes.ProductVersionAnnotation, OutboxSqlTypes.ProductVersion)
            .HasAnnotation(RelationalAnnotationNames.MaxIdentifierLength, OutboxSqlTypes.MaxIdentifierLength);
        modelBuilder.UseIdentityColumns();
        modelBuilder.Entity(typeof(OutboxEntry).FullName!, BuildOutboxEntry);
    }

    private static void BuildOutboxEntry(EntityTypeBuilder entity)
    {
        entity.Property<long>(nameof(OutboxEntry.Id)).ValueGeneratedOnAdd().HasColumnType(OutboxSqlTypes.BigInt).UseIdentityColumn();
        entity.Property<int>(nameof(OutboxEntry.Attempt)).HasColumnType(OutboxSqlTypes.Int32Column);
        entity.Property<DateTimeOffset>(nameof(OutboxEntry.CreatedAt)).HasColumnType(OutboxSqlTypes.DateTimeOffset);
        entity.Property<string>(nameof(OutboxEntry.LastError))
            .HasMaxLength(OutboxColumnLimits.LastErrorMaxLength)
            .HasColumnType(OutboxSqlTypes.NVarChar(OutboxColumnLimits.LastErrorMaxLength));
        entity.Property<DateTimeOffset?>(nameof(OutboxEntry.LockedUntil)).HasColumnType(OutboxSqlTypes.DateTimeOffset);
        entity.Property<DateTimeOffset?>(nameof(OutboxEntry.NotBefore)).HasColumnType(OutboxSqlTypes.DateTimeOffset);
        entity.Property<string>(nameof(OutboxEntry.PayloadJson)).IsRequired().HasColumnType(OutboxSqlTypes.NVarCharMax);
        entity.Property<DateTimeOffset?>(nameof(OutboxEntry.SentAt)).HasColumnType(OutboxSqlTypes.DateTimeOffset);
        entity.Property<int>(nameof(OutboxEntry.Status)).HasColumnType(OutboxSqlTypes.Int32Column);
        entity.Property<string>(nameof(OutboxEntry.Type))
            .IsRequired()
            .HasMaxLength(OutboxLimits.TypeMaxLength)
            .HasColumnType(OutboxSqlTypes.NVarChar(OutboxLimits.TypeMaxLength));
        entity.HasKey(nameof(OutboxEntry.Id));
        entity.HasIndex(nameof(OutboxEntry.Status), nameof(OutboxEntry.CreatedAt));
        entity.ToTable(OutboxTableNames.OutboxMessages, OutboxDbContext.SchemaName);
    }
}

/// <summary>The model snapshot <c>dotnet ef migrations add</c> diffs the next migration against.</summary>
[DbContext(typeof(OutboxDbContext))]
public sealed class OutboxDbContextModelSnapshot : ModelSnapshot
{
    /// <inheritdoc />
    protected override void BuildModel(ModelBuilder modelBuilder) => OutboxMigrationModel.Build(modelBuilder);
}
