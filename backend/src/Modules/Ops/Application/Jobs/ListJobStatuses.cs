using Starter.Application.Results;

namespace Starter.Modules.Ops.Application.Jobs;

/// <summary><c>GET api/ops/jobs</c>: the job catalog with each job's newest run, ordered by key.</summary>
public sealed class ListJobStatuses(IJobStatusSource source)
{
    /// <summary>Lists the jobs.</summary>
    public async Task<Outcome<IReadOnlyList<JobStatus>>> ExecuteAsync(CancellationToken cancellationToken)
    {
        var jobs = await source.ListAsync(cancellationToken);
        IReadOnlyList<JobStatus> ordered = [.. jobs.OrderBy(job => job.Key, StringComparer.Ordinal)];
        return Outcome.Success(ordered);
    }
}
