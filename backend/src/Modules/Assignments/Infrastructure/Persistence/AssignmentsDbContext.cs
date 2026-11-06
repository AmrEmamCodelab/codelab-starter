using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Starter.Infrastructure.Persistence;
using Starter.Modules.Assignments.Domain;

namespace Starter.Modules.Assignments.Infrastructure.Persistence;

/// <summary>The Assignments module's tables, in the <c>assignments</c> schema: the assignment row history.</summary>
public sealed class AssignmentsDbContext(DbContextOptions<AssignmentsDbContext> options) : ModuleDbContext(options)
{
    /// <summary>The database schema of the module.</summary>
    public const string SchemaName = "assignments";

    /// <summary>The assignment rows (open and closed).</summary>
    public DbSet<RecordAssignment> RecordAssignments => Set<RecordAssignment>();

    /// <inheritdoc />
    protected override string Schema => SchemaName;
}

/// <summary>
/// The <c>assignments.RecordAssignments</c> table. Record and user ids are plain <c>bigint</c> columns with no
/// cross-schema foreign key (the Records and Identity modules own those rows).
/// </summary>
public sealed class RecordAssignmentConfiguration : IEntityTypeConfiguration<RecordAssignment>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<RecordAssignment> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        builder.ToTable(AssignmentsTableNames.RecordAssignments);
        builder.HasKey(row => row.Id);
        builder.Property(row => row.Id).UseIdentityColumn();
        builder.Property(row => row.RecordType).HasMaxLength(AssignmentsLimits.RecordTypeMaxLength).IsRequired();
        builder.Ignore(row => row.IsOpen);
        builder.HasIndex(row => row.RecordId)
            .IsUnique()
            .HasFilter(AssignmentsTableNames.OpenRowFilter)
            .HasDatabaseName(AssignmentsTableNames.OneOpenRowPerRecordIndex);
        builder.HasIndex(row => new { row.RecordType, row.AssigneeUserId })
            .HasFilter(AssignmentsTableNames.OpenRowFilter)
            .HasDatabaseName(AssignmentsTableNames.OpenByAssigneeIndex);
        builder.HasIndex(row => new { row.RecordId, row.AssignedAt })
            .HasDatabaseName(AssignmentsTableNames.HistoryIndex);
    }
}

/// <summary>The Assignments table, index and filter names.</summary>
public static class AssignmentsTableNames
{
    /// <summary>The assignment row history.</summary>
    public const string RecordAssignments = "RecordAssignments";

    /// <summary>The filter of the open-row indexes: a row is open while it has no release time.</summary>
    public const string OpenRowFilter = "[ReleasedAt] IS NULL";

    /// <summary>At most one open row per record (the database decides concurrent claims).</summary>
    public const string OneOpenRowPerRecordIndex = "UX_RecordAssignments_RecordId_Open";

    /// <summary>The open rows of a queue by assignee (the "mine" view and the unassigned view's skip list).</summary>
    public const string OpenByAssigneeIndex = "IX_RecordAssignments_RecordType_AssigneeUserId_Open";

    /// <summary>A record's assignment history in time order.</summary>
    public const string HistoryIndex = "IX_RecordAssignments_RecordId_AssignedAt";
}
