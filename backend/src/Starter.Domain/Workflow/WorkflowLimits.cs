namespace Starter.Domain.Workflow;

/// <summary>The workflow engine's limits.</summary>
public static class WorkflowLimits
{
    /// <summary>The longest reason, in characters, after trimming; also the history column size.</summary>
    public const int MaxReasonLength = 1000;
}
