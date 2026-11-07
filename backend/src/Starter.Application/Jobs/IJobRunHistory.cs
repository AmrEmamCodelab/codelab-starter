namespace Starter.Application.Jobs;

/// <summary>
/// Read access to the jobs module's run history for other modules (the Ops job status). Implemented by the jobs module;
/// absent when that module is removed, so a consumer takes it as optional and shows no run.
/// </summary>
public interface IJobRunHistory
{
    /// <summary>The newest run of each of <paramref name="jobKeys"/> that has one, by key; a key that never ran is absent.</summary>
    Task<IReadOnlyDictionary<string, JobRunSummary>> LatestPerJobAsync(IReadOnlyCollection<string> jobKeys, CancellationToken cancellationToken);
}

/// <summary>One run as other modules see it. Identifiers and times only, never an exception message.</summary>
/// <param name="Status">Its state.</param>
/// <param name="Trigger">Why it started.</param>
/// <param name="StartedAt">When it started executing, or null while queued.</param>
/// <param name="FinishedAt">When it ended, or null.</param>
/// <param name="ReferenceCode">The reference code of a failed run's log line, or null.</param>
public sealed record JobRunSummary(JobRunStatus Status, JobTrigger Trigger, DateTimeOffset? StartedAt, DateTimeOffset? FinishedAt, string? ReferenceCode);
