using Starter.Domain.Attachments;

namespace Starter.Modules.ServiceRequests.Domain;

/// <summary>
/// The sample record type as the engine knows it: its key, its number prefix and the attachable record type of its
/// files. Rename these three (and the module) to turn the sample into a project's record type.
/// </summary>
public static class ServiceRequestRecord
{
    /// <summary>The record type key (kebab-case; the <c>{recordType}</c> segment of the Records and queue routes).</summary>
    public const string RecordType = "service-request";

    /// <summary>The reference number prefix: numbers look like <c>SR-2026-001</c>.</summary>
    public const string NumberPrefix = "SR";

    /// <summary>The attachable record type of its files (the same key as the record type).</summary>
    public const string AttachableRecordType = AttachableRecordTypes.ServiceRequest;
}

/// <summary>
/// The statuses of a service request (the <c>Status</c> int the Records kernel stores). Stored values: add a status
/// with the next value, never renumber. <see cref="Approved"/>, <see cref="Rejected"/> and <see cref="Cancelled"/> are
/// terminal.
/// </summary>
public static class ServiceRequestStatuses
{
    /// <summary>Being filled in by the requester (the initial status; no number yet).</summary>
    public const int Draft = 1;

    /// <summary>Submitted and numbered; waiting for a reviewer.</summary>
    public const int Submitted = 2;

    /// <summary>A reviewer is working on it (the SLA runs here).</summary>
    public const int UnderReview = 3;

    /// <summary>Sent back to the requester with a reason.</summary>
    public const int Returned = 4;

    /// <summary>The requester changed it and sent it back; waiting for a reviewer again.</summary>
    public const int Resubmitted = 5;

    /// <summary>Approved (terminal).</summary>
    public const int Approved = 6;

    /// <summary>Rejected with a reason (terminal).</summary>
    public const int Rejected = 7;

    /// <summary>Cancelled by the requester (terminal).</summary>
    public const int Cancelled = 8;

    /// <summary>The statuses in which the requester may still edit the fields and the files.</summary>
    public static IReadOnlySet<int> Editable { get; } = new HashSet<int> { Draft, Returned };
}

/// <summary>The status keys (<c>WorkflowStatusDefinition.Key</c>, the <c>statusKey</c> on the wire; kebab-case).</summary>
public static class ServiceRequestStatusKeys
{
    /// <summary><see cref="ServiceRequestStatuses.Draft"/>.</summary>
    public const string Draft = "draft";

    /// <summary><see cref="ServiceRequestStatuses.Submitted"/>.</summary>
    public const string Submitted = "submitted";

    /// <summary><see cref="ServiceRequestStatuses.UnderReview"/>.</summary>
    public const string UnderReview = "under-review";

    /// <summary><see cref="ServiceRequestStatuses.Returned"/>.</summary>
    public const string Returned = "returned";

    /// <summary><see cref="ServiceRequestStatuses.Resubmitted"/>.</summary>
    public const string Resubmitted = "resubmitted";

    /// <summary><see cref="ServiceRequestStatuses.Approved"/>.</summary>
    public const string Approved = "approved";

    /// <summary><see cref="ServiceRequestStatuses.Rejected"/>.</summary>
    public const string Rejected = "rejected";

    /// <summary><see cref="ServiceRequestStatuses.Cancelled"/>.</summary>
    public const string Cancelled = "cancelled";
}

/// <summary>The action keys of the transition table (the <c>action</c> of the generic transitions endpoint).</summary>
public static class ServiceRequestActions
{
    /// <summary>Draft to Submitted (requester, owner).</summary>
    public const string Submit = "submit";

    /// <summary>Submitted or Resubmitted to Under Review (staff).</summary>
    public const string StartReview = "start-review";

    /// <summary>Under Review to Returned (staff; reason required).</summary>
    public const string Return = "return";

    /// <summary>Returned to Resubmitted (requester, owner).</summary>
    public const string Resubmit = "resubmit";

    /// <summary>Under Review to Approved (staff).</summary>
    public const string Approve = "approve";

    /// <summary>Under Review to Rejected (staff; reason required).</summary>
    public const string Reject = "reject";

    /// <summary>Draft or Submitted to Cancelled (requester, owner; reason optional).</summary>
    public const string Cancel = "cancel";
}
