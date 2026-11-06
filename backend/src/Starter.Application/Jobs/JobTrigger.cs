namespace Starter.Application.Jobs;

/// <summary>Why a job run started (stored and sent as the integer value).</summary>
public enum JobTrigger
{
    /// <summary>The job's recurring schedule.</summary>
    Schedule = 1,

    /// <summary>An administrator's "run now".</summary>
    Manual = 2,
}
