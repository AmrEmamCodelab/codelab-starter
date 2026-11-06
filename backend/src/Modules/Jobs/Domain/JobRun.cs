using Starter.Application.Jobs;
using Starter.Domain.Auditing;

namespace Starter.Modules.Jobs.Domain;

/// <summary>
/// One run of a job (<c>jobs.JobRuns</c>): the actor it ran as (always <see cref="SystemActor.Name"/>), the
/// administrator who pressed "run now" (a manual run), when it ran and how it ended. A failed run keeps a classified
/// error code and the reference code an operator searches the log with, never an exception message. The table is a
/// trail of its own, so it is not audited again.
/// </summary>
[NotAudited]
public sealed class JobRun
{
    private JobRun()
    {
    }

    /// <summary>The internal id (never on the wire; the wire carries a PublicId).</summary>
    public long Id { get; private set; }

    /// <summary>The job's key.</summary>
    public string JobKey { get; private set; } = string.Empty;

    /// <summary>The run's state (stored as the integer value).</summary>
    public JobRunStatus Status { get; private set; }

    /// <summary>Why the run started (stored as the integer value).</summary>
    public JobTrigger Trigger { get; private set; }

    /// <summary>The actor the run's work is recorded under: always <see cref="SystemActor.Name"/>.</summary>
    public string Actor { get; private set; } = SystemActor.Name;

    /// <summary>The token subject of the administrator who triggered a manual run, or null (a scheduled run).</summary>
    public Guid? TriggeredBySubjectId { get; private set; }

    /// <summary>When the run was created (triggered, or picked up from the schedule).</summary>
    public DateTimeOffset QueuedAt { get; private set; }

    /// <summary>When a job server started executing it, or null while queued.</summary>
    public DateTimeOffset? StartedAt { get; private set; }

    /// <summary>When it ended, or null while queued or running.</summary>
    public DateTimeOffset? FinishedAt { get; private set; }

    /// <summary>The correlation id of the execution (every log line of the run carries it), or null while queued.</summary>
    public string? CorrelationId { get; private set; }

    /// <summary>The classified error code of a failed run, or null.</summary>
    public string? ErrorCode { get; private set; }

    /// <summary>The reference code of a failed execution (its log line's), or null.</summary>
    public string? ReferenceCode { get; private set; }

    /// <summary>True once the run succeeded or failed.</summary>
    public bool IsFinished => Status is JobRunStatus.Succeeded or JobRunStatus.Failed;

    /// <summary>A new queued run of <paramref name="jobKey"/>.</summary>
    /// <exception cref="ArgumentException">The key is malformed or the trigger is unknown.</exception>
    public static JobRun Queue(string jobKey, JobTrigger trigger, Guid? triggeredBySubjectId, DateTimeOffset at)
    {
        if (!JobKeyFormat.IsValid(jobKey))
        {
            throw new ArgumentException("A job key must match JobKeyFormat.", nameof(jobKey));
        }

        if (!Enum.IsDefined(trigger))
        {
            throw new ArgumentException("Unknown job trigger.", nameof(trigger));
        }

        return new JobRun
        {
            JobKey = jobKey,
            Status = JobRunStatus.Queued,
            Trigger = trigger,
            TriggeredBySubjectId = triggeredBySubjectId,
            QueuedAt = at,
        };
    }

    /// <summary>
    /// Starts executing. Allowed from Queued, and from Running when the scheduler delivers the same run again after its
    /// node stopped (the run restarts with a new correlation id).
    /// </summary>
    /// <exception cref="InvalidOperationException">The run already finished.</exception>
    public void Start(string correlationId, DateTimeOffset at)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(correlationId);
        EnsureNotFinished();
        Status = JobRunStatus.Running;
        CorrelationId = Cut(correlationId, JobsLimits.CorrelationIdMaxLength);
        StartedAt = at;
    }

    /// <summary>Ends a running run successfully.</summary>
    /// <exception cref="InvalidOperationException">The run is not running.</exception>
    public void Succeed(DateTimeOffset at)
    {
        if (Status is not JobRunStatus.Running)
        {
            throw new InvalidOperationException($"Only a running job run can succeed (run {Id} is {(int)Status}).");
        }

        Status = JobRunStatus.Succeeded;
        FinishedAt = at;
    }

    /// <summary>Ends a queued or running run as failed with <paramref name="failure"/>.</summary>
    /// <exception cref="InvalidOperationException">The run already finished.</exception>
    public void Fail(JobRunFailure failure, DateTimeOffset at)
    {
        ArgumentNullException.ThrowIfNull(failure);
        ArgumentException.ThrowIfNullOrWhiteSpace(failure.ErrorCode);
        EnsureNotFinished();
        Status = JobRunStatus.Failed;
        ErrorCode = Cut(failure.ErrorCode, JobsLimits.ErrorCodeMaxLength);
        ReferenceCode = failure.ReferenceCode is { } reference ? Cut(reference, JobsLimits.ReferenceCodeMaxLength) : null;
        FinishedAt = at;
    }

    private void EnsureNotFinished()
    {
        if (IsFinished)
        {
            throw new InvalidOperationException($"Job run {Id} already finished ({(int)Status}).");
        }
    }

    private static string Cut(string value, int maxLength) => value.Length > maxLength ? value[..maxLength] : value;
}

/// <summary>Why a run failed: a classified code (never an exception message) and the log's reference code, if any.</summary>
/// <param name="ErrorCode">The error code (<c>INTERNAL_ERROR</c>, a classifier's code or a <c>JobRunFailureCodes</c> value).</param>
/// <param name="ReferenceCode">The reference code of the failure's log line, or null when the job never ran.</param>
public sealed record JobRunFailure(string ErrorCode, string? ReferenceCode);
