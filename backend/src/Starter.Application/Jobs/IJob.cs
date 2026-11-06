namespace Starter.Application.Jobs;

/// <summary>
/// One background job. Every implementation is registered with <c>services.AddStarterJob&lt;TJob&gt;()</c>
/// (Starter.Infrastructure.Jobs), resolved per run in its own DI scope, and listed on the admin Jobs screen. The jobs
/// module schedules it when <see cref="Cron"/> is set, runs it on a manual trigger, records every run (actor
/// <see cref="SystemActor.Name"/>) and runs it inside <see cref="ISystemActorScope"/>, so nothing a job writes is ever
/// attributed to a person or a role.
/// </summary>
/// <remarks>
/// A run may overlap neither itself nor a crashed earlier attempt's side effects: the jobs module never runs one key
/// twice at the same time, but a run that died mid-way is not resumed, so <see cref="RunAsync"/> must be safe to run
/// again from the start (idempotent). Throw to fail the run: the run row keeps only the classified error code and a
/// reference code, never the exception message.
/// </remarks>
public interface IJob
{
    /// <summary>The stable key (<see cref="JobKeyFormat"/>), from a constants class; also the recurring schedule id.</summary>
    string Key { get; }

    /// <summary>The job's name on the admin screen, in Arabic.</summary>
    string NameAr { get; }

    /// <summary>The job's name on the admin screen, in English.</summary>
    string NameEn { get; }

    /// <summary>
    /// The schedule as a five-field cron expression in the jobs time zone (<c>Jobs:TimeZoneId</c>), from a constants
    /// class; null for a job that only runs when an administrator triggers it.
    /// </summary>
    string? Cron { get; }

    /// <summary>Does the work of one run. Honour <paramref name="cancellationToken"/>: it fires when the node stops.</summary>
    Task RunAsync(JobRunContext context, CancellationToken cancellationToken);
}

/// <summary>What a job knows about the run it executes. Identifiers only.</summary>
/// <param name="JobKey">The job's key.</param>
/// <param name="RunId">The internal id of the run row (never on the wire).</param>
/// <param name="Trigger">Why the run started.</param>
/// <param name="CorrelationId">The run's correlation id: every log line of the run carries it.</param>
public sealed record JobRunContext(string JobKey, long RunId, JobTrigger Trigger, string CorrelationId);
