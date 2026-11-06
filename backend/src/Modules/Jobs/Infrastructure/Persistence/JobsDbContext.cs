using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Starter.Infrastructure.Persistence;
using Starter.Modules.Jobs.Domain;

namespace Starter.Modules.Jobs.Infrastructure.Persistence;

/// <summary>The jobs module's tables, in the <c>jobs</c> schema: the run history.</summary>
public sealed class JobsDbContext(DbContextOptions<JobsDbContext> options) : ModuleDbContext(options)
{
    /// <summary>The database schema of the module.</summary>
    public const string SchemaName = "jobs";

    /// <summary>The job runs.</summary>
    public DbSet<JobRun> JobRuns => Set<JobRun>();

    /// <inheritdoc />
    protected override string Schema => SchemaName;
}

/// <summary>The <c>jobs.JobRuns</c> table.</summary>
public sealed class JobRunConfiguration : IEntityTypeConfiguration<JobRun>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<JobRun> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        builder.ToTable(JobsTableNames.JobRuns);
        builder.HasKey(run => run.Id);
        builder.Property(run => run.JobKey).HasMaxLength(JobsLimits.JobKeyMaxLength).IsRequired();
        builder.Property(run => run.Actor).HasMaxLength(JobsLimits.ActorMaxLength).IsRequired();
        builder.Property(run => run.CorrelationId).HasMaxLength(JobsLimits.CorrelationIdMaxLength);
        builder.Property(run => run.ErrorCode).HasMaxLength(JobsLimits.ErrorCodeMaxLength);
        builder.Property(run => run.ReferenceCode).HasMaxLength(JobsLimits.ReferenceCodeMaxLength);
        builder.HasIndex(run => new { run.JobKey, run.Id }).HasDatabaseName(JobsTableNames.JobKeyIdIndex);
        builder.Ignore(run => run.IsFinished);
    }
}

/// <summary>The jobs table and index names.</summary>
public static class JobsTableNames
{
    /// <summary>The run history table.</summary>
    public const string JobRuns = "JobRuns";

    /// <summary>The index behind "newest runs of a job" and "is a run of this job active".</summary>
    public const string JobKeyIdIndex = "IX_JobRuns_JobKey_Id";
}
