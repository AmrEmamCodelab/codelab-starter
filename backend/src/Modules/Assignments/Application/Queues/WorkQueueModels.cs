using Starter.Application.Records;

namespace Starter.Modules.Assignments.Application.Queues;

/// <summary>One record of a work queue page.</summary>
/// <param name="Id">The internal record id (the wire carries its PublicId).</param>
/// <param name="Number">The record's reference number.</param>
/// <param name="Status">The status value.</param>
/// <param name="StatusKey">The status's wire key (<c>under-review</c>).</param>
/// <param name="SubmittedAt">When the record was first submitted.</param>
/// <param name="OwnerDisplayName">The requester's display name.</param>
/// <param name="Assignee">Who holds the record, or null when nobody does.</param>
/// <param name="Sla">The record's SLA badge, or null when no SLA runs (or no SLA reader is registered).</param>
public sealed record WorkQueueEntry(
    long Id,
    string Number,
    int Status,
    string StatusKey,
    DateTimeOffset SubmittedAt,
    string OwnerDisplayName,
    AssignmentSnapshot? Assignee,
    SlaBadge? Sla);

/// <summary>One page of a work queue.</summary>
/// <param name="Items">The records of the page, in the directory's order (newest submit first).</param>
/// <param name="Page">The 1-based page.</param>
/// <param name="PageSize">The page size used.</param>
/// <param name="Total">The number of matching records on every page.</param>
public sealed record WorkQueuePage(IReadOnlyList<WorkQueueEntry> Items, int Page, int PageSize, int Total)
{
    /// <summary>The empty page of a request that reaches no record.</summary>
    public static WorkQueuePage Empty(RecordPageRequest page)
    {
        ArgumentNullException.ThrowIfNull(page);
        return new WorkQueuePage([], page.Page, page.PageSize, Total: 0);
    }
}
