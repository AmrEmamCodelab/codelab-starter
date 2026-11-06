using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Starter.Infrastructure.Auditing;
using Starter.Infrastructure.Persistence;
using Starter.Modules.Records.Domain;

namespace Starter.Modules.Records.Infrastructure.Persistence;

/// <summary>The Records module's tables, in the <c>records</c> schema: the record base rows and their timeline.</summary>
public sealed class RecordsDbContext(DbContextOptions<RecordsDbContext> options) : ModuleDbContext(options)
{
    /// <summary>The database schema of the module.</summary>
    public const string SchemaName = "records";

    /// <summary>The record base rows.</summary>
    public DbSet<Record> Records => Set<Record>();

    /// <summary>The timeline rows.</summary>
    public DbSet<RecordHistory> History => Set<RecordHistory>();

    /// <inheritdoc />
    protected override string Schema => SchemaName;
}

/// <summary>The Records table names.</summary>
public static class RecordsTableNames
{
    /// <summary>The record base rows.</summary>
    public const string Records = "Records";

    /// <summary>The timeline rows.</summary>
    public const string RecordHistory = "RecordHistory";
}

/// <summary>
/// The <c>records.Records</c> table: a unique number once taken (filtered), a rowversion, and the list indexes
/// (owner's records by type, a type's records by status).
/// </summary>
public sealed class RecordConfiguration : IEntityTypeConfiguration<Record>
{
    /// <summary>The filter of the unique number index (drafts have no number yet).</summary>
    public const string NumberIndexFilter = "[Number] IS NOT NULL";

    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<Record> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        builder.ToTable(RecordsTableNames.Records);
        builder.HasKey(record => record.Id);
        builder.Property(record => record.Id).UseIdentityColumn();
        builder.Property(record => record.RecordType).HasMaxLength(RecordColumnLimits.RecordTypeMaxLength).IsRequired();
        builder.Property(record => record.Number).HasMaxLength(RecordColumnLimits.NumberMaxLength);
        builder.Property(record => record.RowVersion).IsRowVersion();
        builder.HasIndex(record => record.Number).IsUnique().HasFilter(NumberIndexFilter);
        builder.HasIndex(record => new { record.RecordType, record.OwnerUserId });
        builder.HasIndex(record => new { record.RecordType, record.Status });
    }
}

/// <summary>
/// The <c>records.RecordHistory</c> table, mapped by <see cref="RecordHistoryMapping.HasRecordHistory{TRecord, THistory}"/>:
/// ON DELETE RESTRICT to the record and the (RecordId, At) timeline index.
/// </summary>
public sealed class RecordHistoryConfiguration : IEntityTypeConfiguration<RecordHistory>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<RecordHistory> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        builder.ToTable(RecordsTableNames.RecordHistory);
        builder.HasRecordHistory<Record, RecordHistory>();
    }
}
