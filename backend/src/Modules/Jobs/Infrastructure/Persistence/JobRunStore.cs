using Microsoft.EntityFrameworkCore;
using Starter.Application.Jobs;
using Starter.Modules.Jobs.Application;
using Starter.Modules.Jobs.Domain;

namespace Starter.Modules.Jobs.Infrastructure.Persistence;

/// <summary><see cref="IJobRuns"/> on <see cref="JobsDbContext"/>.</summary>
public sealed class JobRunStore(JobsDbContext context) : IJobRuns
{
    /// <inheritdoc />
    public void Add(JobRun run) => context.JobRuns.Add(run);

    /// <inheritdoc />
    public Task<JobRun?> FindAsync(long runId, CancellationToken cancellationToken) =>
        context.JobRuns.SingleOrDefaultAsync(run => run.Id == runId, cancellationToken);

    /// <inheritdoc />
    public Task<bool> HasActiveRunAsync(string jobKey, DateTimeOffset since, CancellationToken cancellationToken) =>
        context.JobRuns.AnyAsync(
            run => run.JobKey == jobKey
                && (run.Status == JobRunStatus.Queued || run.Status == JobRunStatus.Running)
                && run.QueuedAt >= since,
            cancellationToken);

    /// <inheritdoc />
    public async Task<IReadOnlyDictionary<string, JobRun>> LatestPerJobAsync(
        IReadOnlyCollection<string> jobKeys,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(jobKeys);
        var keys = jobKeys.ToList();
        var newestIds = await context.JobRuns.AsNoTracking()
            .Where(run => keys.Contains(run.JobKey))
            .GroupBy(run => run.JobKey)
            .Select(group => group.Max(run => run.Id))
            .ToListAsync(cancellationToken);
        var newest = await context.JobRuns.AsNoTracking()
            .Where(run => newestIds.Contains(run.Id))
            .ToListAsync(cancellationToken);
        return newest.ToDictionary(run => run.JobKey, StringComparer.Ordinal);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<JobRun>> ListRecentAsync(string jobKey, int take, CancellationToken cancellationToken) =>
        await context.JobRuns.AsNoTracking()
            .Where(run => run.JobKey == jobKey)
            .OrderByDescending(run => run.Id)
            .Take(take)
            .ToListAsync(cancellationToken);

    /// <inheritdoc />
    public Task<int> AbandonOtherRunningAsync(JobRun current, DateTimeOffset at, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(current);
        return context.JobRuns
            .Where(run => run.JobKey == current.JobKey && run.Id != current.Id && run.Status == JobRunStatus.Running)
            .ExecuteUpdateAsync(
                update => update
                    .SetProperty(run => run.Status, JobRunStatus.Failed)
                    .SetProperty(run => run.ErrorCode, JobRunFailureCodes.RunAbandoned)
                    .SetProperty(run => run.FinishedAt, at),
                cancellationToken);
    }

    /// <inheritdoc />
    public Task SaveChangesAsync(CancellationToken cancellationToken) => context.SaveChangesAsync(cancellationToken);
}
