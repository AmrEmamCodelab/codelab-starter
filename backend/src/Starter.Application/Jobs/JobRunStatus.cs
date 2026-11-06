namespace Starter.Application.Jobs;

/// <summary>The state of one job run (stored and sent as the integer value).</summary>
public enum JobRunStatus
{
    /// <summary>Triggered by an administrator and waiting for a job server to pick it up.</summary>
    Queued = 1,

    /// <summary>Executing on a job server.</summary>
    Running = 2,

    /// <summary>Finished without an error.</summary>
    Succeeded = 3,

    /// <summary>Finished with an error, was refused (overlap, unknown job) or was abandoned by a node that stopped.</summary>
    Failed = 4,
}
