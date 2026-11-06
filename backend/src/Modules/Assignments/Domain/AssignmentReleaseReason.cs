namespace Starter.Modules.Assignments.Domain;

/// <summary>Why an assignment row was closed. Stored and sent as the integer value.</summary>
public enum AssignmentReleaseReason
{
    /// <summary>The assignee gave the record back to the queue.</summary>
    Released = 1,

    /// <summary>A supervisor handed the record to another user (a new row opened at the same time).</summary>
    Reassigned = 2,

    /// <summary>The record reached a terminal status, so nobody works it any more.</summary>
    RecordClosed = 3,
}
