using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Starter.Modules.Deadlines.Domain;

namespace Starter.Modules.Deadlines.Infrastructure.Persistence.Migrations;

/// <summary>The migration identifiers and constraint names of the deadlines schema.</summary>
public static class DeadlinesMigrationIds
{
    /// <summary>The id of the first migration.</summary>
    public const string InitialCreate = "20261106230000_InitialCreate";

    /// <summary>The primary key of <c>deadlines.RecordDeadlines</c>.</summary>
    public const string RecordDeadlinesPrimaryKey = "PK_RecordDeadlines";
}

/// <summary>The SQL Server store types and annotations the deadlines migrations write.</summary>
public static class DeadlinesSqlTypes
{
    /// <summary>32-bit integer.</summary>
    public const string Int32Column = "int";

    /// <summary>64-bit integer.</summary>
    public const string BigInt = "bigint";

    /// <summary>Date, time and offset.</summary>
    public const string DateTimeOffset = "datetimeoffset";

    /// <summary>The SQL Server identity annotation.</summary>
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
/// The deadlines model as of the latest migration: the target model of <see cref="InitialCreate"/> and the model
/// snapshot that <c>dotnet ef migrations add</c> diffs against. A later migration freezes its own copy before this one
/// changes.
/// </summary>
public static class DeadlinesMigrationModel
{
    /// <summary>Builds the model.</summary>
    public static void Build(ModelBuilder modelBuilder)
    {
        ArgumentNullException.ThrowIfNull(modelBuilder);
        modelBuilder
            .HasDefaultSchema(DeadlinesDbContext.SchemaName)
            .HasAnnotation(DeadlinesSqlTypes.ProductVersionAnnotation, DeadlinesSqlTypes.ProductVersion)
            .HasAnnotation(RelationalAnnotationNames.MaxIdentifierLength, DeadlinesSqlTypes.MaxIdentifierLength);
        modelBuilder.UseIdentityColumns();
        modelBuilder.Entity(typeof(RecordDeadline).FullName!, BuildRecordDeadline);
    }

    private static void BuildRecordDeadline(EntityTypeBuilder entity)
    {
        entity.Property<long>(nameof(RecordDeadline.Id)).ValueGeneratedOnAdd().HasColumnType(DeadlinesSqlTypes.BigInt).UseIdentityColumn();
        Instant(entity, nameof(RecordDeadline.BreachedAt), required: false);
        Instant(entity, nameof(RecordDeadline.ClosedAt), required: false);
        Instant(entity, nameof(RecordDeadline.DueAt), required: true);
        Instant(entity, nameof(RecordDeadline.DueSoonAt), required: true);
        Instant(entity, nameof(RecordDeadline.EscalatedAt), required: false);
        entity.Property<long>(nameof(RecordDeadline.RecordId)).HasColumnType(DeadlinesSqlTypes.BigInt);
        entity.Property<string>(nameof(RecordDeadline.RecordType))
            .IsRequired()
            .HasMaxLength(DeadlinesLimits.RecordTypeMaxLength)
            .HasColumnType(DeadlinesSqlTypes.NVarChar(DeadlinesLimits.RecordTypeMaxLength));
        Instant(entity, nameof(RecordDeadline.StartedAt), required: true);
        entity.Property<int>(nameof(RecordDeadline.Status)).HasColumnType(DeadlinesSqlTypes.Int32Column);
        entity.HasKey(nameof(RecordDeadline.Id));
        entity.HasIndex(nameof(RecordDeadline.DueAt))
            .HasDatabaseName(DeadlinesTableNames.OpenByDueAtIndex)
            .HasFilter(DeadlinesTableNames.OpenFilter);
        entity.HasIndex(nameof(RecordDeadline.RecordId))
            .IsUnique()
            .HasDatabaseName(DeadlinesTableNames.OneOpenPerRecordIndex)
            .HasFilter(DeadlinesTableNames.OpenFilter);
        entity.ToTable(DeadlinesTableNames.RecordDeadlines, DeadlinesDbContext.SchemaName);
    }

    private static void Instant(EntityTypeBuilder entity, string name, bool required)
    {
        if (required)
        {
            entity.Property<DateTimeOffset>(name).HasColumnType(DeadlinesSqlTypes.DateTimeOffset);
            return;
        }

        entity.Property<DateTimeOffset?>(name).HasColumnType(DeadlinesSqlTypes.DateTimeOffset);
    }
}

/// <summary>The model snapshot <c>dotnet ef migrations add</c> diffs the next migration against.</summary>
[DbContext(typeof(DeadlinesDbContext))]
public sealed class DeadlinesDbContextModelSnapshot : ModelSnapshot
{
    /// <inheritdoc />
    protected override void BuildModel(ModelBuilder modelBuilder) => DeadlinesMigrationModel.Build(modelBuilder);
}
