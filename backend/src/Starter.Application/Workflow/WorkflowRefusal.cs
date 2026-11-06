namespace Starter.Application.Workflow;

/// <summary>Why the engine refused a transition. Map it to the wire with <see cref="WorkflowRefusals.ToRefusal"/>.</summary>
public enum WorkflowRefusal
{
    /// <summary>The catalog declares no action with this key (404).</summary>
    ActionUnknown = 1,

    /// <summary>The action exists, and the caller may take it, but not from the record's current status (409).</summary>
    NotAllowedFromStatus = 2,

    /// <summary>The caller lacks the permission, or the ownership, the action requires (403).</summary>
    Forbidden = 3,

    /// <summary>The action requires a reason and none, or only whitespace, was given (400).</summary>
    ReasonRequired = 4,

    /// <summary>The reason is longer than <see cref="Starter.Domain.Workflow.WorkflowLimits.MaxReasonLength"/> (400).</summary>
    ReasonTooLong = 5,
}
