using Codelab.Security.PublicIds;
using Microsoft.AspNetCore.Mvc;
using Starter.Application.Jobs;
using Starter.Application.Results;
using Starter.Infrastructure.Results;
using Starter.Modules.Jobs.Application;
using Starter.Modules.Jobs.Application.Admin;
using Starter.Modules.Jobs.Domain;

namespace Starter.Modules.Jobs.Api;

/// <summary>One registered job on the wire, with its newest run.</summary>
/// <param name="Key">The stable key.</param>
/// <param name="NameAr">The name in Arabic.</param>
/// <param name="NameEn">The name in English.</param>
/// <param name="Cron">The five-field cron schedule (jobs time zone), or null for a manual-only job.</param>
/// <param name="LastRun">The newest run, or null when it never ran.</param>
public sealed record JobResponse(string Key, string NameAr, string NameEn, string? Cron, JobRunResponse? LastRun)
{
    /// <summary>The wire shape of <paramref name="summary"/>.</summary>
    public static JobResponse From(JobSummary summary)
    {
        ArgumentNullException.ThrowIfNull(summary);
        var job = summary.Job;
        return new JobResponse(
            job.Key,
            job.NameAr,
            job.NameEn,
            job.Cron,
            summary.LastRun is { } run ? JobRunResponse.From(run) : null);
    }

    /// <summary>The wire shape of a list of jobs, in order.</summary>
    public static IReadOnlyList<JobResponse> ListFrom(IReadOnlyList<JobSummary> summaries)
    {
        ArgumentNullException.ThrowIfNull(summaries);
        return [.. summaries.Select(From)];
    }
}

/// <summary>One job run on the wire. Times are ISO 8601 UTC; enums are their integer values.</summary>
/// <param name="Id">The run's PublicId.</param>
/// <param name="Status">Queued = 1, Running = 2, Succeeded = 3, Failed = 4.</param>
/// <param name="Trigger">Schedule = 1, Manual = 2.</param>
/// <param name="Actor">The actor the run's work is recorded under: always <c>SYSTEM</c>.</param>
/// <param name="TriggeredBy">
/// The display name of the administrator who ran it now, or null for a scheduled run (or an account that no longer
/// exists). Never the internal subject id.
/// </param>
/// <param name="StartedAt">When it started executing, or null while queued.</param>
/// <param name="FinishedAt">When it ended, or null.</param>
/// <param name="ReferenceCode">The reference code of a failed execution (quote it to find the log line), or null.</param>
public sealed record JobRunResponse(
    [property: WireId] PublicId<JobRunIds> Id,
    JobRunStatus Status,
    JobTrigger Trigger,
    string Actor,
    string? TriggeredBy,
    DateTimeOffset? StartedAt,
    DateTimeOffset? FinishedAt,
    string? ReferenceCode)
{
    /// <summary>The wire shape of <paramref name="view"/>.</summary>
    public static JobRunResponse From(JobRunView view)
    {
        ArgumentNullException.ThrowIfNull(view);
        var run = view.Run;
        return new JobRunResponse(
            PublicId<JobRunIds>.From(run.Id),
            run.Status,
            run.Trigger,
            run.Actor,
            view.TriggeredByName,
            run.StartedAt?.ToUniversalTime(),
            run.FinishedAt?.ToUniversalTime(),
            run.ReferenceCode);
    }

    /// <summary>The wire shape of a list of runs, in order.</summary>
    public static IReadOnlyList<JobRunResponse> ListFrom(IReadOnlyList<JobRunView> runs)
    {
        ArgumentNullException.ThrowIfNull(runs);
        return [.. runs.Select(From)];
    }
}

/// <summary>The answer to "run now": the queued run's PublicId (its outcome appears in the run history).</summary>
/// <param name="RunId">The queued run's PublicId.</param>
public sealed record TriggeredJobRunResponse([property: WireId] PublicId<JobRunIds> RunId)
{
    /// <summary>The wire shape of the queued <paramref name="run"/>.</summary>
    public static TriggeredJobRunResponse From(JobRun run)
    {
        ArgumentNullException.ThrowIfNull(run);
        return new TriggeredJobRunResponse(PublicId<JobRunIds>.From(run.Id));
    }
}

/// <summary>The jobs module's outcome mapping beyond the kernel's 200 and 204.</summary>
public static class JobsResults
{
    /// <summary>202 with <paramref name="map"/> of the value, or the refusal's problem+json.</summary>
    public static ActionResult<TResponse> ToAcceptedResult<T, TResponse>(this Outcome<T> outcome, Func<T, TResponse> map)
    {
        ArgumentNullException.ThrowIfNull(outcome);
        ArgumentNullException.ThrowIfNull(map);
        return outcome.IsSuccess ? new AcceptedResult(location: (string?)null, value: map(outcome.Value)) : outcome.Refusal.ToActionResult();
    }
}
