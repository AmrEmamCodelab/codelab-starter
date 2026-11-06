using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Starter.Infrastructure.Persistence;
using Starter.Modules.Deadlines.Domain;

namespace Starter.Modules.Deadlines.Infrastructure.Persistence;

/// <summary>The deadlines module's tables, in the <c>deadlines</c> schema: the record deadlines.</summary>
public sealed class DeadlinesDbContext(DbContextOptions<DeadlinesDbContext> options) : ModuleDbContext(options)
{
    /// <summary>The database schema of the module.</summary>
    public const string SchemaName = "deadlines";

    /// <summary>The record deadlines.</summary>
    public DbSet<RecordDeadline> RecordDeadlines => Set<RecordDeadline>();

    /// <inheritdoc />
    protected override string Schema => SchemaName;
}

/// <summary>The <c>deadlines.RecordDeadlines</c> table.</summary>
public sealed class RecordDeadlineConfiguration : IEntityTypeConfiguration<RecordDeadline>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<RecordDeadline> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        builder.ToTable(DeadlinesTableNames.RecordDeadlines);
        builder.HasKey(deadline => deadline.Id);
        builder.Property(deadline => deadline.Id).UseIdentityColumn();
        builder.Property(deadline => deadline.RecordType).HasMaxLength(DeadlinesLimits.RecordTypeMaxLength).IsRequired();
        builder.HasIndex(deadline => deadline.RecordId)
            .IsUnique()
            .HasFilter(DeadlinesTableNames.OpenFilter)
            .HasDatabaseName(DeadlinesTableNames.OneOpenPerRecordIndex);
        builder.HasIndex(deadline => deadline.DueAt)
            .HasFilter(DeadlinesTableNames.OpenFilter)
            .HasDatabaseName(DeadlinesTableNames.OpenByDueAtIndex);
        builder.Ignore(deadline => deadline.IsOpen);
        builder.Ignore(deadline => deadline.AwaitsEscalation);
    }
}

/// <summary>The deadlines table, index and filter names.</summary>
public static class DeadlinesTableNames
{
    /// <summary>The record deadlines table.</summary>
    public const string RecordDeadlines = "RecordDeadlines";

    /// <summary>At most one open deadline per record (filtered on <see cref="OpenFilter"/>); also the badge lookup.</summary>
    public const string OneOpenPerRecordIndex = "UX_RecordDeadlines_RecordId_Open";

    /// <summary>The open deadlines by deadline: the scan's "due now" and "awaiting escalation" reads.</summary>
    public const string OpenByDueAtIndex = "IX_RecordDeadlines_DueAt_Open";

    /// <summary>The SQL filter of the open deadlines.</summary>
    public const string OpenFilter = "[ClosedAt] IS NULL";
}
