namespace Starter.Modules.Assignments.Application;

/// <summary>
/// The Assignments module's error codes. The client renders <c>errors.&lt;CODE&gt;</c> from its ar and en bundles. A
/// record that does not exist, is of another record type or lies outside the caller's organization scope answers the
/// kernel <c>RecordsErrorCodes.NotFound</c> (404), the same code the record detail uses.
/// </summary>
public static class AssignmentsErrorCodes
{
    /// <summary>The record already has an assignee (409): claim it only after it is released.</summary>
    public const string AlreadyClaimed = "ASSIGNMENTS_ALREADY_CLAIMED";

    /// <summary>Only the current assignee may release the record (403).</summary>
    public const string NotAssignee = "ASSIGNMENTS_NOT_ASSIGNEE";

    /// <summary>The chosen user does not hold the queue permission for the record's organization, or is inactive (400).</summary>
    public const string AssigneeNotEligible = "ASSIGNMENTS_ASSIGNEE_NOT_ELIGIBLE";

    /// <summary>No module registers this record type with a workflow, so it has no queue (404).</summary>
    public const string RecordTypeUnknown = "ASSIGNMENTS_RECORD_TYPE_UNKNOWN";

    /// <summary>The caller does not hold the record type's queue permission (403).</summary>
    public const string QueueForbidden = "ASSIGNMENTS_QUEUE_FORBIDDEN";

    /// <summary>The record is a draft or closed, so it is not in the queue (409).</summary>
    public const string NotAssignable = "ASSIGNMENTS_NOT_ASSIGNABLE";

    /// <summary>The caller's token names no known user account (403).</summary>
    public const string ActorUnknown = "ASSIGNMENTS_ACTOR_UNKNOWN";

    // scaffold:error-codes
}
