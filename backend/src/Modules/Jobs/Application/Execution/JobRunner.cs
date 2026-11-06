using Microsoft.Extensions.Logging;
using Starter.Modules.Jobs.Application.Catalog;

namespace Starter.Modules.Jobs.Application.Execution;

/// <summary>
/// The two entry points the job scheduler calls, each in its own DI scope: a scheduled tick of a job and a manual run
/// an administrator queued. Both take the job's run lock first, so a job never runs twice at the same time; then
/// <see cref="JobExecution"/> runs and records it.
/// </summary>
public sealed partial class JobRunner(
    JobCatalog catalog,
    IJobRuns runs,
    IJobRunLock runLock,
    JobExecution execution,
    ILogger<JobRunner> logger)
{
    /// <summary>
    /// One scheduled tick of <paramref name="jobKey"/>: records a Schedule run and executes it. A tick for a key no
    /// longer registered, or while another run of the job executes, is skipped without a row (the next tick comes).
    /// </summary>
    public async Task RunScheduledAsync(string jobKey, CancellationToken cancellationToken)
    {
        var job = catalog.Find(jobKey);
        if (job is null)
        {
            LogUnknownScheduledJob(logger, jobKey);
            return;
        }

        await using var held = await runLock.TryAcquireAsync(jobKey, cancellationToken);
        if (held is null)
        {
            LogTickSkipped(logger, jobKey);
            return;
        }

        var run = execution.AddScheduledRun(jobKey);
        await execution.RunAsync(job, run, cancellationToken);
    }

    /// <summary>
    /// Executes the queued manual run <paramref name="runId"/>. A missing or finished run is skipped (a repeated
    /// delivery); a run whose job is no longer registered, or whose job is executing, ends as Failed without running.
    /// </summary>
    public async Task RunManualAsync(long runId, CancellationToken cancellationToken)
    {
        var run = await runs.FindAsync(runId, cancellationToken);
        if (run is null || run.IsFinished)
        {
            LogManualRunSkipped(logger, runId);
            return;
        }

        var job = catalog.Find(run.JobKey);
        if (job is null)
        {
            await execution.RefuseAsync(run, JobRunFailureCodes.JobNotRegistered, cancellationToken);
            return;
        }

        await using var held = await runLock.TryAcquireAsync(run.JobKey, cancellationToken);
        if (held is null)
        {
            await execution.RefuseAsync(run, JobRunFailureCodes.RunOverlapped, cancellationToken);
            return;
        }

        await execution.RunAsync(job, run, cancellationToken);
    }

    [LoggerMessage(EventId = 6316, Level = LogLevel.Warning, Message = "Scheduled tick of job {JobKey} skipped: no such job is registered")]
    private static partial void LogUnknownScheduledJob(ILogger logger, string jobKey);

    [LoggerMessage(EventId = 6317, Level = LogLevel.Information, Message = "Scheduled tick of job {JobKey} skipped: a run is executing")]
    private static partial void LogTickSkipped(ILogger logger, string jobKey);

    [LoggerMessage(EventId = 6318, Level = LogLevel.Information, Message = "Manual job run {RunId} skipped: missing or already finished")]
    private static partial void LogManualRunSkipped(ILogger logger, long runId);
}
