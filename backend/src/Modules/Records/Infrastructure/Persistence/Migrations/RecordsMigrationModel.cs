using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Starter.Domain.Auditing;
using Starter.Modules.Records.Domain;

namespace Starter.Modules.Records.Infrastructure.Persistence.Migrations;

/// <summary>The migration identifiers and constraint names of the Records schema.</summary>
public static class RecordsMigrationIds
{
    /// <summary>The id of the first migration.</summary>
    public const string InitialCreate = "20261106180000_InitialCreate";

    /// <summary>The primary key of <c>records.Records</c>.</summary>
    public const string RecordsPrimaryKey = "PK_Records";

    /// <summary>The unique (filtered) index of the reference number.</summary>
    public const string NumberIndex = "IX_Records_Number";

    /// <summary>The "my records" index (record type, owner).</summary>
    public const string OwnerIndex = "IX_Records_RecordType_OwnerUserId";

    /// <summary>The list-by-status index (record type, status).</summary>
    public const string StatusIndex = "IX_Records_RecordType_Status";

    /// <summary>The primary key of <c>records.RecordHistory</c>.</summary>
    public const string HistoryPrimaryKey = "PK_RecordHistory";

    /// <summary>The foreign key from a timeline entry to its record (Restrict: a record with history cannot be deleted).</summary>
    public const string HistoryRecordForeignKey = "FK_RecordHistory_Records_RecordId";

    /// <summary>The timeline index (record, then time).</summary>
    public const string HistoryTimelineIndex = "IX_RecordHistory_RecordId_At";
}

/// <summary>The SQL Server store types and annotations the Records migrations write.</summary>
public static class RecordsSqlTypes
{
    /// <summary>32-bit integer.</summary>
    public const string Int32Column = "int";

    /// <summary>64-bit integer.</summary>
    public const string BigInt = "bigint";

    /// <summary>Date, time and offset.</summary>
    public const string DateTimeOffset = "datetimeoffset";

    /// <summary>The SQL Server row version.</summary>
    public const string RowVersion = "rowversion";

    /// <summary>The identity column annotation.</summary>
    public const string IdentityAnnotation = "SqlServer:Identity";

    /// <summary>Identity seed 1, increment 1.</summary>
    public const string IdentitySeedIncrement = "1, 1";

    /// <summary>The model annotation that records the EF Core version.</summary>
    public const string ProductVersionAnnotation = "ProductVersion";

    /// <summary>The EF Core version of the model.</summary>
    public const string ProductVersion = "10.0.14";

    /// <summary>The longest SQL Server identifier.</summary>
    public const int MaxIdentifierLength = 128;

    /// <summary>A Unicode string column of <paramref name="length"/> characters.</summary>
    public static string NVarChar(int length) => $"nvarchar({length})";
}

/// <summary>
/// The Records model as of the latest migration: the target model of <see cref="InitialCreate"/> and the model snapshot
/// that <c>dotnet ef migrations add</c> diffs against. A later migration freezes its own copy before this one changes.
/// </summary>
public static class RecordsMigrationModel
{
    /// <summary>Builds the model.</summary>
    public static void Build(ModelBuilder modelBuilder)
    {
        ArgumentNullException.ThrowIfNull(modelBuilder);
        modelBuilder
            .HasDefaultSchema(RecordsDbContext.SchemaName)
            .HasAnnotation(RecordsSqlTypes.ProductVersionAnnotation, RecordsSqlTypes.ProductVersion)
            .HasAnnotation(RelationalAnnotationNames.MaxIdentifierLength, RecordsSqlTypes.MaxIdentifierLength);
        modelBuilder.UseIdentityColumns();
        modelBuilder.Entity(typeof(Record).FullName!, BuildRecord);
        modelBuilder.Entity(typeof(RecordHistory).FullName!, BuildHistory);
        modelBuilder.Entity(typeof(RecordHistory).FullName!, entry => entry
            .HasOne(typeof(Record).FullName!, navigationName: null)
            .WithMany()
            .HasForeignKey(nameof(RecordHistoryEntry.RecordId))
            .OnDelete(DeleteBehavior.Restrict)
            .IsRequired());
    }

    private static void BuildRecord(EntityTypeBuilder entity)
    {
        entity.Property<long>(nameof(Record.Id)).ValueGeneratedOnAdd().HasColumnType(RecordsSqlTypes.BigInt).UseIdentityColumn();
        entity.Property<DateTimeOffset>(nameof(Record.CreatedAt)).HasColumnType(RecordsSqlTypes.DateTimeOffset);
        entity.Property<string>(nameof(Record.Number))
            .HasMaxLength(RecordColumnLimits.NumberMaxLength).HasColumnType(RecordsSqlTypes.NVarChar(RecordColumnLimits.NumberMaxLength));
        entity.Property<long?>(nameof(Record.OrganizationId)).HasColumnType(RecordsSqlTypes.BigInt);
        entity.Property<long>(nameof(Record.OwnerUserId)).HasColumnType(RecordsSqlTypes.BigInt);
        Text(entity, nameof(Record.RecordType), RecordColumnLimits.RecordTypeMaxLength);
        entity.Property<byte[]>(nameof(Record.RowVersion))
            .IsConcurrencyToken().ValueGeneratedOnAddOrUpdate().IsRequired().HasColumnType(RecordsSqlTypes.RowVersion);
        entity.Property<int>(nameof(Record.Status)).HasColumnType(RecordsSqlTypes.Int32Column);
        entity.Property<DateTimeOffset?>(nameof(Record.SubmittedAt)).HasColumnType(RecordsSqlTypes.DateTimeOffset);
        entity.Property<DateTimeOffset>(nameof(Record.UpdatedAt)).HasColumnType(RecordsSqlTypes.DateTimeOffset);
        entity.HasKey(nameof(Record.Id));
        entity.HasIndex(nameof(Record.Number)).IsUnique().HasFilter(RecordConfiguration.NumberIndexFilter);
        entity.HasIndex(nameof(Record.RecordType), nameof(Record.OwnerUserId));
        entity.HasIndex(nameof(Record.RecordType), nameof(Record.Status));
        entity.ToTable(RecordsTableNames.Records, RecordsDbContext.SchemaName);
    }

    private static void BuildHistory(EntityTypeBuilder entity)
    {
        entity.Property<long>(nameof(RecordHistoryEntry.Id)).ValueGeneratedOnAdd().HasColumnType(RecordsSqlTypes.BigInt).UseIdentityColumn();
        Text(entity, nameof(RecordHistoryEntry.Action), RecordHistoryLimits.ActionMaxLength);
        Text(entity, nameof(RecordHistoryEntry.ActorName), RecordHistoryLimits.ActorNameMaxLength);
        entity.Property<long?>(nameof(RecordHistoryEntry.ActorUserId)).HasColumnType(RecordsSqlTypes.BigInt);
        entity.Property<DateTimeOffset>(nameof(RecordHistoryEntry.At)).HasColumnType(RecordsSqlTypes.DateTimeOffset);
        entity.Property<int?>(nameof(RecordHistoryEntry.FromStatus)).HasColumnType(RecordsSqlTypes.Int32Column);
        entity.Property<string>(nameof(RecordHistoryEntry.Reason))
            .HasMaxLength(RecordHistoryLimits.ReasonMaxLength).HasColumnType(RecordsSqlTypes.NVarChar(RecordHistoryLimits.ReasonMaxLength));
        entity.Property<long>(nameof(RecordHistoryEntry.RecordId)).HasColumnType(RecordsSqlTypes.BigInt);
        entity.Property<int>(nameof(RecordHistory.Side)).HasColumnType(RecordsSqlTypes.Int32Column);
        entity.Property<int?>(nameof(RecordHistoryEntry.ToStatus)).HasColumnType(RecordsSqlTypes.Int32Column);
        entity.HasKey(nameof(RecordHistoryEntry.Id));
        entity.HasIndex(nameof(RecordHistoryEntry.RecordId), nameof(RecordHistoryEntry.At));
        entity.ToTable(RecordsTableNames.RecordHistory, RecordsDbContext.SchemaName);
    }

    private static void Text(EntityTypeBuilder entity, string name, int maxLength) =>
        entity.Property<string>(name).IsRequired().HasMaxLength(maxLength).HasColumnType(RecordsSqlTypes.NVarChar(maxLength));
}

/// <summary>The model snapshot <c>dotnet ef migrations add</c> diffs the next migration against.</summary>
[DbContext(typeof(RecordsDbContext))]
public sealed class RecordsDbContextModelSnapshot : ModelSnapshot
{
    /// <inheritdoc />
    protected override void BuildModel(ModelBuilder modelBuilder) => RecordsMigrationModel.Build(modelBuilder);
}
