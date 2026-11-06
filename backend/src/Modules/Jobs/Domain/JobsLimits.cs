using Starter.Application.Jobs;

namespace Starter.Modules.Jobs.Domain;

/// <summary>The column sizes and bounds of the jobs module (one owner for each number).</summary>
public static class JobsLimits
{
    /// <summary>The longest job key (<see cref="JobKeyFormat.MaxLength"/>).</summary>
    public const int JobKeyMaxLength = JobKeyFormat.MaxLength;

    /// <summary>The longest recorded actor (<see cref="SystemActor.Name"/> fits).</summary>
    public const int ActorMaxLength = 32;

    /// <summary>The longest correlation id (Codelab.Observability <c>CorrelationId.MaxLength</c>).</summary>
    public const int CorrelationIdMaxLength = 64;

    /// <summary>The longest stored error code (a classified code such as <c>INTERNAL_ERROR</c>).</summary>
    public const int ErrorCodeMaxLength = 64;

    /// <summary>The longest stored reference code (derived from the correlation id, so never longer).</summary>
    public const int ReferenceCodeMaxLength = CorrelationIdMaxLength;

    /// <summary>The runs the history endpoint returns when the caller does not say.</summary>
    public const int DefaultHistoryTake = 20;

    /// <summary>The most runs the history endpoint returns.</summary>
    public const int MaxHistoryTake = 100;

    /// <summary>
    /// How long a Queued or Running run blocks a new manual trigger of its job. A run older than this is treated as
    /// lost (its node stopped), so an administrator is never locked out of a job for good.
    /// </summary>
    public static readonly TimeSpan ActiveRunWindow = TimeSpan.FromHours(1);
}
