using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Starter.Modules.Assignments.Domain;

namespace Starter.Modules.Assignments.Infrastructure.Persistence.Migrations;

/// <summary>The migration identifiers and constraint names of the assignments schema.</summary>
public static class AssignmentsMigrationIds
{
    /// <summary>The id of the first migration.</summary>
    public const string InitialCreate = "20261106140000_InitialCreate";

    /// <summary>The primary key of <c>assignments.RecordAssignments</c>.</summary>
    public const string RecordAssignmentsPrimaryKey = "PK_RecordAssignments";
}

/// <summary>The SQL Server store types and annotations the assignments migrations write.</summary>
public static class AssignmentsSqlTypes
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
/// The assignments model as of the latest migration: the target model of <see cref="InitialCreate"/> and the model
/// snapshot that <c>dotnet ef migrations add</c> diffs against. A later migration freezes its own copy before this one
/// changes.
/// </summary>
public static class AssignmentsMigrationModel
{
    /// <summary>Builds the model.</summary>
    public static void Build(ModelBuilder modelBuilder)
    {
        ArgumentNullException.ThrowIfNull(modelBuilder);
        modelBuilder
            .HasDefaultSchema(AssignmentsDbContext.SchemaName)
            .HasAnnotation(AssignmentsSqlTypes.ProductVersionAnnotation, AssignmentsSqlTypes.ProductVersion)
            .HasAnnotation(RelationalAnnotationNames.MaxIdentifierLength, AssignmentsSqlTypes.MaxIdentifierLength);
        modelBuilder.UseIdentityColumns();
        modelBuilder.Entity(typeof(RecordAssignment).FullName!, BuildRecordAssignment);
    }

    private static void BuildRecordAssignment(EntityTypeBuilder entity)
    {
        entity.Property<long>(nameof(RecordAssignment.Id)).ValueGeneratedOnAdd().HasColumnType(AssignmentsSqlTypes.BigInt).UseIdentityColumn();
        entity.Property<DateTimeOffset>(nameof(RecordAssignment.AssignedAt)).HasColumnType(AssignmentsSqlTypes.DateTimeOffset);
        entity.Property<long>(nameof(RecordAssignment.AssignedByUserId)).HasColumnType(AssignmentsSqlTypes.BigInt);
        entity.Property<long>(nameof(RecordAssignment.AssigneeUserId)).HasColumnType(AssignmentsSqlTypes.BigInt);
        entity.Property<long>(nameof(RecordAssignment.RecordId)).HasColumnType(AssignmentsSqlTypes.BigInt);
        entity.Property<string>(nameof(RecordAssignment.RecordType))
            .IsRequired()
            .HasMaxLength(AssignmentsLimits.RecordTypeMaxLength)
            .HasColumnType(AssignmentsSqlTypes.NVarChar(AssignmentsLimits.RecordTypeMaxLength));
        entity.Property<int?>(nameof(RecordAssignment.ReleaseReason)).HasColumnType(AssignmentsSqlTypes.Int32Column);
        entity.Property<DateTimeOffset?>(nameof(RecordAssignment.ReleasedAt)).HasColumnType(AssignmentsSqlTypes.DateTimeOffset);
        entity.Property<long?>(nameof(RecordAssignment.ReleasedByUserId)).HasColumnType(AssignmentsSqlTypes.BigInt);
        entity.HasKey(nameof(RecordAssignment.Id));
        entity.HasIndex(nameof(RecordAssignment.RecordId))
            .IsUnique()
            .HasDatabaseName(AssignmentsTableNames.OneOpenRowPerRecordIndex)
            .HasFilter(AssignmentsTableNames.OpenRowFilter);
        entity.HasIndex(nameof(RecordAssignment.RecordId), nameof(RecordAssignment.AssignedAt))
            .HasDatabaseName(AssignmentsTableNames.HistoryIndex);
        entity.HasIndex(nameof(RecordAssignment.RecordType), nameof(RecordAssignment.AssigneeUserId))
            .HasDatabaseName(AssignmentsTableNames.OpenByAssigneeIndex)
            .HasFilter(AssignmentsTableNames.OpenRowFilter);
        entity.ToTable(AssignmentsTableNames.RecordAssignments, AssignmentsDbContext.SchemaName);
    }
}

/// <summary>The model snapshot <c>dotnet ef migrations add</c> diffs the next migration against.</summary>
[DbContext(typeof(AssignmentsDbContext))]
public sealed class AssignmentsDbContextModelSnapshot : ModelSnapshot
{
    /// <inheritdoc />
    protected override void BuildModel(ModelBuilder modelBuilder) => AssignmentsMigrationModel.Build(modelBuilder);
}
