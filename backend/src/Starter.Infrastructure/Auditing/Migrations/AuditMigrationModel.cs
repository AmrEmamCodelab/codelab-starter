using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Starter.Infrastructure.Auditing.Migrations;

/// <summary>The migration identifiers and constraint names of the audit schema.</summary>
public static class AuditMigrationIds
{
    /// <summary>The id of the first migration.</summary>
    public const string InitialCreate = "20261106090000_InitialCreate";

    /// <summary>The primary key of <c>audit.AuditEntries</c>.</summary>
    public const string AuditEntriesPrimaryKey = "PK_AuditEntries";

    /// <summary>The index the viewer filters one entity's trail through, newest first.</summary>
    public const string AuditEntriesEntityIndex = "IX_AuditEntries_EntityType_EntityKey_Id";
}

/// <summary>The SQL Server store types and annotations the audit migrations write.</summary>
public static class AuditSqlTypes
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
/// The audit model as of the latest migration: the target model of <see cref="InitialCreate"/> and the model snapshot
/// that <c>dotnet ef migrations add</c> diffs against. A later migration freezes its own copy before this one changes.
/// </summary>
public static class AuditMigrationModel
{
    /// <summary>Builds the model.</summary>
    public static void Build(ModelBuilder modelBuilder)
    {
        ArgumentNullException.ThrowIfNull(modelBuilder);
        modelBuilder
            .HasDefaultSchema(AuditDbContext.SchemaName)
            .HasAnnotation(AuditSqlTypes.ProductVersionAnnotation, AuditSqlTypes.ProductVersion)
            .HasAnnotation(RelationalAnnotationNames.MaxIdentifierLength, AuditSqlTypes.MaxIdentifierLength);
        modelBuilder.UseIdentityColumns();
        modelBuilder.Entity(typeof(AuditEntry).FullName!, BuildAuditEntry);
    }

    private static void BuildAuditEntry(EntityTypeBuilder entity)
    {
        entity.Property<long>(nameof(AuditEntry.Id)).ValueGeneratedOnAdd().HasColumnType(AuditSqlTypes.BigInt).UseIdentityColumn();
        entity.Property<int>(nameof(AuditEntry.Action)).HasColumnType(AuditSqlTypes.Int32Column);
        entity.Property<DateTimeOffset>(nameof(AuditEntry.At)).HasColumnType(AuditSqlTypes.DateTimeOffset);
        entity.Property<string>(nameof(AuditEntry.Actor)).IsRequired().HasMaxLength(AuditLimits.ActorMaxLength).HasColumnType(AuditSqlTypes.NVarChar(AuditLimits.ActorMaxLength));
        entity.Property<string>(nameof(AuditEntry.ChangesJson)).IsRequired().HasColumnType(AuditSqlTypes.NVarCharMax);
        entity.Property<string>(nameof(AuditEntry.CorrelationId)).HasMaxLength(AuditLimits.CorrelationIdMaxLength).HasColumnType(AuditSqlTypes.NVarChar(AuditLimits.CorrelationIdMaxLength));
        entity.Property<string>(nameof(AuditEntry.EntityKey)).HasMaxLength(AuditLimits.EntityKeyMaxLength).HasColumnType(AuditSqlTypes.NVarChar(AuditLimits.EntityKeyMaxLength));
        entity.Property<string>(nameof(AuditEntry.EntityType)).IsRequired().HasMaxLength(AuditLimits.EntityTypeMaxLength).HasColumnType(AuditSqlTypes.NVarChar(AuditLimits.EntityTypeMaxLength));
        entity.HasKey(nameof(AuditEntry.Id));
        entity.HasIndex(nameof(AuditEntry.EntityType), nameof(AuditEntry.EntityKey), nameof(AuditEntry.Id));
        entity.ToTable(AuditTableNames.AuditEntries, AuditDbContext.SchemaName);
    }
}

/// <summary>The model snapshot <c>dotnet ef migrations add</c> diffs the next migration against.</summary>
[DbContext(typeof(AuditDbContext))]
public sealed class AuditDbContextModelSnapshot : ModelSnapshot
{
    /// <inheritdoc />
    protected override void BuildModel(ModelBuilder modelBuilder) => AuditMigrationModel.Build(modelBuilder);
}
