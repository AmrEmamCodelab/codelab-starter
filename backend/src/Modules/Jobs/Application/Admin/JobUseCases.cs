using Starter.Application.Jobs;
using Starter.Application.Results;
using Starter.Application.Users;
using Starter.Application.Validation;
using Starter.Modules.Jobs.Application.Catalog;
using Starter.Modules.Jobs.Domain;

namespace Starter.Modules.Jobs.Application.Admin;

/// <summary>A registered job with its newest run (null when it never ran).</summary>
/// <param name="Job">The job.</param>
/// <param name="LastRun">Its newest run, or null.</param>
public sealed record JobSummary(IJob Job, JobRunView? LastRun);

/// <summary>A run as the history shows it.</summary>
/// <param name="Run">The run.</param>
/// <param name="TriggeredByName">
/// The display name of the administrator who ran it now, or null for a scheduled run or a subject that no longer has
/// an account. The internal subject id never leaves the module.
/// </param>
public sealed record JobRunView(JobRun Run, string? TriggeredByName);

/// <summary>
/// Turns runs into <see cref="JobRunView"/>s: each distinct triggering subject is resolved once through the kernel
/// <see cref="IUserDirectory"/> (Identity), so this module never references Identity.
/// </summary>
public sealed class JobRunViews(IUserDirectory users)
{
    /// <summary>The views of <paramref name="runs"/>, in order.</summary>
    public async Task<IReadOnlyList<JobRunView>> OfAsync(IReadOnlyList<JobRun> runs, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(runs);
        var names = new Dictionary<Guid, string?>();
        foreach (var subject in runs.Select(run => run.TriggeredBySubjectId).OfType<Guid>().Distinct())
        {
            names[subject] = (await users.FindBySubjectAsync(subject, cancellationToken))?.DisplayName;
        }

        return [.. runs.Select(run => new JobRunView(run, run.TriggeredBySubjectId is { } subject ? names[subject] : null))];
    }
}

/// <summary>The query of the run history.</summary>
/// <param name="Take">How many runs, newest first: 1 to <see cref="JobsLimits.MaxHistoryTake"/>; <see cref="JobsLimits.DefaultHistoryTake"/> when absent.</param>
public sealed record JobRunsQuery(int? Take);

/// <summary>Lists every registered job with its newest run, ordered by key.</summary>
public sealed class ListJobs(JobCatalog catalog, IJobRuns runs, JobRunViews views)
{
    /// <summary>Runs the use case (never refused).</summary>
    public async Task<Outcome<IReadOnlyList<JobSummary>>> ExecuteAsync(CancellationToken cancellationToken)
    {
        var latest = await runs.LatestPerJobAsync(catalog.Keys, cancellationToken);
        var lastRuns = (await views.OfAsync([.. latest.Values], cancellationToken)).ToDictionary(view => view.Run.JobKey, StringComparer.Ordinal);
        IReadOnlyList<JobSummary> summaries = [.. catalog.Jobs.Select(job => new JobSummary(job, lastRuns.GetValueOrDefault(job.Key)))];
        return Outcome.Success(summaries);
    }
}

/// <summary>
/// Records a manual run of a job (Queued, triggered by the caller) and hands it to the job scheduler; a job server runs
/// it as the system actor. Refused with 404 for an unknown key and 409 while the job has a queued or running run
/// younger than <see cref="JobsLimits.ActiveRunWindow"/>.
/// </summary>
public sealed class TriggerJobRun(JobCatalog catalog, IJobRuns runs, IJobRunDispatcher dispatcher, ICurrentUserContext currentUser, TimeProvider clock)
{
    /// <summary>Runs the use case; the value is the queued run.</summary>
    public async Task<Outcome<JobRun>> ExecuteAsync(string jobKey, CancellationToken cancellationToken)
    {
        if (catalog.Find(jobKey) is null)
        {
            return Refusal.NotFound(JobsErrorCodes.JobNotFound);
        }

        var now = clock.GetUtcNow();
        if (await runs.HasActiveRunAsync(jobKey, since: now - JobsLimits.ActiveRunWindow, cancellationToken))
        {
            return Refusal.Conflict(JobsErrorCodes.RunInProgress);
        }

        var run = JobRun.Queue(jobKey, JobTrigger.Manual, currentUser.SubjectId, now);
        runs.Add(run);
        await runs.SaveChangesAsync(cancellationToken);
        await DispatchAsync(run);
        return run;
    }

    private async Task DispatchAsync(JobRun run)
    {
        try
        {
            dispatcher.DispatchManualRun(run.Id);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Load-bearing: closes the recorded run, so it does not block the job's next trigger, then fails the call.
            run.Fail(new JobRunFailure(JobRunFailureCodes.EnqueueFailed, ReferenceCode: null), clock.GetUtcNow());
            await runs.SaveChangesAsync(CancellationToken.None);
            throw;
        }
    }
}

/// <summary>Lists the newest runs of one job, newest first. Refused with 404 for an unknown key, 400 for a bad <c>take</c>.</summary>
public sealed class ListJobRuns(JobCatalog catalog, IJobRuns runs, JobRunViews views)
{
    /// <summary>Runs the use case.</summary>
    public async Task<Outcome<IReadOnlyList<JobRunView>>> ExecuteAsync(string jobKey, JobRunsQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        var take = query.Take ?? JobsLimits.DefaultHistoryTake;
        if (take is < 1 or > JobsLimits.MaxHistoryTake)
        {
            return Refusal.Invalid(new ValidationErrors().Add(FieldNames.Of(nameof(JobRunsQuery.Take)), JobsErrorCodes.TakeOutOfRange));
        }

        if (catalog.Find(jobKey) is null)
        {
            return Refusal.NotFound(JobsErrorCodes.JobNotFound);
        }

        return Outcome.Success(await views.OfAsync(await runs.ListRecentAsync(jobKey, take, cancellationToken), cancellationToken));
    }
}
