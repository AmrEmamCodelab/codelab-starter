using Starter.Application.Jobs;
using Starter.Modules.Ops.Application;

namespace Starter.Modules.Ops.Infrastructure.Jobs;

/// <summary>
/// The job catalog from the kernel <see cref="IJob"/> registrations (every job a module adds with
/// <c>AddStarterJob</c>): key, names and schedule, with the newest run of each job from the kernel
/// <see cref="IJobRunHistory"/> (the jobs module's run history). A job that never ran has no
/// <see cref="JobStatus.LastRun"/>; neither has any job when the jobs module is removed (no history is registered).
/// </summary>
public sealed class JobCatalogStatusSource(IEnumerable<IJob> jobs, IJobRunHistory? history = null) : IJobStatusSource
{
    /// <inheritdoc />
    public async Task<IReadOnlyList<JobStatus>> ListAsync(CancellationToken cancellationToken)
    {
        var catalog = jobs.ToList();
        var latest = history is null
            ? new Dictionary<string, JobRunSummary>(StringComparer.Ordinal)
            : await history.LatestPerJobAsync([.. catalog.Select(job => job.Key)], cancellationToken);
        return [.. catalog.Select(job => new JobStatus(job.Key, job.NameAr, job.NameEn, job.Cron, LastRunOf(latest, job.Key)))];
    }

    private static JobLastRun? LastRunOf(IReadOnlyDictionary<string, JobRunSummary> latest, string jobKey) =>
        latest.TryGetValue(jobKey, out var run)
            ? new JobLastRun(run.Status, run.Trigger, run.StartedAt, run.FinishedAt, run.ReferenceCode)
            : null;
}
