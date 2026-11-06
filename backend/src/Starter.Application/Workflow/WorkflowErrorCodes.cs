namespace Starter.Application.Workflow;

/// <summary>
/// The workflow error codes on the wire (problem+json <c>code</c>). The client renders <c>errors.&lt;CODE&gt;</c> from
/// its ar and en bundles.
/// </summary>
public static class WorkflowErrorCodes
{
    /// <summary>The action is not allowed from the record's current status (409).</summary>
    public const string ActionNotAllowed = "WORKFLOW_ACTION_NOT_ALLOWED";

    /// <summary>The caller may not take the action (403).</summary>
    public const string Forbidden = "WORKFLOW_FORBIDDEN";

    /// <summary>The action needs a reason (400, with the reason field's error).</summary>
    public const string ReasonRequired = "WORKFLOW_REASON_REQUIRED";

    /// <summary>The reason is too long (400, with the reason field's error).</summary>
    public const string ReasonTooLong = "WORKFLOW_REASON_TOO_LONG";

    /// <summary>The record type has no action with this key (404).</summary>
    public const string ActionUnknown = "WORKFLOW_ACTION_UNKNOWN";
}
