using Starter.Application.Jobs;
using Starter.Modules.Jobs.Application;
using Starter.Modules.Jobs.Domain;

namespace Starter.Modules.Jobs.Infrastructure.Persistence;

/// <summary>
/// The kernel <see cref="IJobRunHistory"/> over the run history (<see cref="IJobRuns"/>): the newest run of each job as
/// a <see cref="JobRunSummary"/>. Never the error code's text or the triggering administrator.
/// </summary>
public sealed class JobRunHistory(IJobRuns runs) : IJobRunHistory
{
    /// <inheritdoc />
    public async Task<IReadOnlyDictionary<string, JobRunSummary>> LatestPerJobAsync(IReadOnlyCollection<string> jobKeys, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(jobKeys);
        if (jobKeys.Count == 0)
        {
            return new Dictionary<string, JobRunSummary>(StringComparer.Ordinal);
        }

        var latest = await runs.LatestPerJobAsync(jobKeys, cancellationToken);
        return latest.ToDictionary(pair => pair.Key, pair => Summarize(pair.Value), StringComparer.Ordinal);
    }

    /// <summary>The summary of <paramref name="run"/>.</summary>
    public static JobRunSummary Summarize(JobRun run)
    {
        ArgumentNullException.ThrowIfNull(run);
        return new JobRunSummary(run.Status, run.Trigger, run.StartedAt, run.FinishedAt, run.ReferenceCode);
    }
}
