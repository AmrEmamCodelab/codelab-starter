using System.Diagnostics;
using Codelab.Observability;
using Microsoft.Extensions.Logging;
using Starter.Application.Jobs;
using Starter.Modules.Jobs.Domain;

namespace Starter.Modules.Jobs.Application.Execution;

/// <summary>
/// Executes one run of a job and records it: inside a system scope (<see cref="ISystemActorScope"/>: no person is the
/// actor) and a fresh correlation scope, it starts the run row, closes the rows a stopped node left Running, calls the
/// job and ends the row as Succeeded or Failed. A failure is stored as its classified code
/// (<see cref="IFailureCodeResolver"/>) and the reference code of its one log line, never the exception message.
/// Callers hold the job's run lock.
/// </summary>
public sealed partial class JobExecution(
    IJobRuns runs,
    ISystemActorScope systemActor,
    IFailureCodeResolver failureCodes,
    TimeProvider clock,
    ILogger<JobExecution> logger)
{
    /// <summary>Records a new scheduled run of <paramref name="jobKey"/> (written by <see cref="RunAsync"/>).</summary>
    public JobRun AddScheduledRun(string jobKey)
    {
        var run = JobRun.Queue(jobKey, JobTrigger.Schedule, triggeredBySubjectId: null, clock.GetUtcNow());
        runs.Add(run);
        return run;
    }

    /// <summary>Ends <paramref name="run"/> as Failed with <paramref name="failureCode"/> without running the job.</summary>
    public async Task RefuseAsync(JobRun run, string failureCode, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(run);
        run.Fail(new JobRunFailure(failureCode, ReferenceCode: null), clock.GetUtcNow());
        await runs.SaveChangesAsync(cancellationToken);
        LogRefused(logger, run.JobKey, run.Id, failureCode);
    }

    /// <summary>
    /// Runs <paramref name="job"/> for <paramref name="run"/> and records the outcome. Cancellation of
    /// <paramref name="cancellationToken"/> (the node stopping) propagates and leaves the row Running; the next run of
    /// the job closes it as abandoned.
    /// </summary>
    public async Task RunAsync(IJob job, JobRun run, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(job);
        ArgumentNullException.ThrowIfNull(run);
        var correlationId = ActivityTraceId.CreateRandom().ToHexString();
        using var system = systemActor.BeginSystemScope();
        using var correlation = CorrelationScope.Begin(correlationId);
        await StartAsync(run, correlationId, cancellationToken);
        var failure = await InvokeAsync(job, run, correlationId, cancellationToken);
        var finishedAt = clock.GetUtcNow();
        if (failure is null)
        {
            run.Succeed(finishedAt);
        }
        else
        {
            run.Fail(failure, finishedAt);
        }

        // The job has finished: its outcome is recorded even when the node is stopping now.
        await runs.SaveChangesAsync(CancellationToken.None);
        LogFinished(logger, run.JobKey, run.Id, run.Status);
    }

    private async Task StartAsync(JobRun run, string correlationId, CancellationToken cancellationToken)
    {
        var now = clock.GetUtcNow();
        run.Start(correlationId, now);
        await runs.SaveChangesAsync(cancellationToken);
        var abandoned = await runs.AbandonOtherRunningAsync(run, now, cancellationToken);
        if (abandoned > 0)
        {
            LogAbandoned(logger, run.JobKey, abandoned);
        }

        LogStarted(logger, run.JobKey, run.Id, run.Trigger);
    }

    private async Task<JobRunFailure?> InvokeAsync(IJob job, JobRun run, string correlationId, CancellationToken cancellationToken)
    {
        try
        {
            await job.RunAsync(new JobRunContext(run.JobKey, run.Id, run.Trigger, correlationId), cancellationToken);
            return null;
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            // Load-bearing (ERROR-HANDLING row 7): records the run row with the classified code, never ex.Message.
            var failure = failureCodes.Resolve(ex);
            var referenceCode = ReferenceCode.From(correlationId);
            LogFailed(logger, failure.Level, ex, run.JobKey, run.Id, failure.Code, referenceCode);
            return new JobRunFailure(failure.Code, referenceCode);
        }
    }

    [LoggerMessage(EventId = 6311, Level = LogLevel.Information, Message = "Job {JobKey} run {RunId} started ({Trigger})")]
    private static partial void LogStarted(ILogger logger, string jobKey, long runId, JobTrigger trigger);

    [LoggerMessage(EventId = 6312, Level = LogLevel.Information, Message = "Job {JobKey} run {RunId} finished ({Status})")]
    private static partial void LogFinished(ILogger logger, string jobKey, long runId, JobRunStatus status);

    [LoggerMessage(EventId = 6313, Message = "Job {JobKey} run {RunId} failed {Code} {ReferenceCode}")]
    private static partial void LogFailed(ILogger logger, LogLevel level, Exception exception, string jobKey, long runId, string code, string referenceCode);

    [LoggerMessage(EventId = 6314, Level = LogLevel.Warning, Message = "Job {JobKey} run {RunId} did not start: {Code}")]
    private static partial void LogRefused(ILogger logger, string jobKey, long runId, string code);

    [LoggerMessage(EventId = 6315, Level = LogLevel.Warning, Message = "Job {JobKey}: {Count} run(s) left Running by a stopped node marked abandoned")]
    private static partial void LogAbandoned(ILogger logger, string jobKey, int count);
}
