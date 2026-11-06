using Starter.Modules.Jobs.Domain;

namespace Starter.Modules.Jobs.Application;

/// <summary>The job run history (<c>jobs.JobRuns</c>) and its unit of work.</summary>
public interface IJobRuns
{
    /// <summary>Tracks a new run; it is written by the next <see cref="SaveChangesAsync"/>.</summary>
    void Add(JobRun run);

    /// <summary>The run with this internal id (tracked), or null.</summary>
    Task<JobRun?> FindAsync(long runId, CancellationToken cancellationToken);

    /// <summary>True when <paramref name="jobKey"/> has a Queued or Running run created at or after <paramref name="since"/>.</summary>
    Task<bool> HasActiveRunAsync(string jobKey, DateTimeOffset since, CancellationToken cancellationToken);

    /// <summary>The newest run of each of <paramref name="jobKeys"/> that has one, by key (no tracking).</summary>
    Task<IReadOnlyDictionary<string, JobRun>> LatestPerJobAsync(IReadOnlyCollection<string> jobKeys, CancellationToken cancellationToken);

    /// <summary>The newest <paramref name="take"/> runs of <paramref name="jobKey"/>, newest first (no tracking).</summary>
    Task<IReadOnlyList<JobRun>> ListRecentAsync(string jobKey, int take, CancellationToken cancellationToken);

    /// <summary>
    /// Marks every other Running run of the run's job as Failed (<see cref="JobRunFailureCodes.RunAbandoned"/>) and
    /// returns how many. Call it only while holding the job's run lock: no other run of the job can then be executing,
    /// so a Running row is one whose node stopped. Writes immediately, without <see cref="SaveChangesAsync"/>.
    /// </summary>
    Task<int> AbandonOtherRunningAsync(JobRun current, DateTimeOffset at, CancellationToken cancellationToken);

    /// <summary>Writes the tracked changes.</summary>
    Task SaveChangesAsync(CancellationToken cancellationToken);
}

/// <summary>Hands a recorded manual run to the job scheduler, which executes it on a job server.</summary>
public interface IJobRunDispatcher
{
    /// <summary>Enqueues the run with this internal id; returns once the scheduler has stored it.</summary>
    void DispatchManualRun(long runId);
}

/// <summary>A lock per job key across every node, so one job never runs twice at the same time.</summary>
public interface IJobRunLock
{
    /// <summary>The held lock (dispose it to release), or null when another run of <paramref name="jobKey"/> holds it.</summary>
    Task<IAsyncDisposable?> TryAcquireAsync(string jobKey, CancellationToken cancellationToken);
}
