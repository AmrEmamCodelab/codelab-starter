namespace Starter.Modules.Jobs.Application;

/// <summary>
/// The jobs module's wire error codes and field message codes. The client renders <c>errors.&lt;CODE&gt;</c> from its
/// ar and en bundles, for a refusal code and for a field message alike.
/// </summary>
public static class JobsErrorCodes
{
    /// <summary>No registered job has this key (404).</summary>
    public const string JobNotFound = "JOBS_JOB_NOT_FOUND";

    /// <summary>The job already has a queued or running run (409); its result appears in the run history.</summary>
    public const string RunInProgress = "JOBS_RUN_IN_PROGRESS";

    /// <summary>
    /// Field message on <c>take</c>: outside 1 to <c>JobsLimits.MaxHistoryTake</c> (with <c>VALIDATION_FAILED</c>, 400).
    /// </summary>
    public const string TakeOutOfRange = "JOBS_TAKE_OUT_OF_RANGE";

    // scaffold:error-codes
}

/// <summary>
/// The error codes a run row stores when the job itself never ran (a failed execution stores the classified code of
/// its exception instead). Operator-facing: they reach the database and the log, not the wire.
/// </summary>
public static class JobRunFailureCodes
{
    /// <summary>A manual run reached a node whose catalog has no job with its key (the job was removed or renamed).</summary>
    public const string JobNotRegistered = "JOBS_JOB_NOT_REGISTERED";

    /// <summary>A manual run found another run of the same job executing, so it did not start.</summary>
    public const string RunOverlapped = "JOBS_RUN_OVERLAPPED";

    /// <summary>A run was still marked Running when the next run of its job started: its node stopped mid-run.</summary>
    public const string RunAbandoned = "JOBS_RUN_ABANDONED";

    /// <summary>The run was recorded but could not be handed to the job scheduler.</summary>
    public const string EnqueueFailed = "JOBS_ENQUEUE_FAILED";
}
