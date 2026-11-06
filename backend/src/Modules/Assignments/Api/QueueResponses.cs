using Codelab.Security.PublicIds;
using Starter.Application.Records;
using Starter.Modules.Assignments.Application;
using Starter.Modules.Assignments.Application.Queues;

namespace Starter.Modules.Assignments.Api;

/// <summary>Who holds a queue record.</summary>
/// <param name="UserId">The assignee's PublicId (family <c>users</c>).</param>
/// <param name="DisplayName">The assignee's display name.</param>
/// <param name="AssignedAt">When the current assignment started.</param>
public sealed record QueueAssigneeResponse(PublicId<AssigneeUserIds> UserId, string DisplayName, DateTimeOffset AssignedAt)
{
    /// <summary>The wire form of <paramref name="snapshot"/>, or null when nobody holds the record.</summary>
    public static QueueAssigneeResponse? From(AssignmentSnapshot? snapshot) =>
        snapshot is null
            ? null
            : new QueueAssigneeResponse(PublicId<AssigneeUserIds>.From(snapshot.AssigneeUserId), snapshot.AssigneeDisplayName, snapshot.AssignedAt);
}

/// <summary>A queue record's SLA badge.</summary>
/// <param name="State">On track (1), due soon (2) or breached (3).</param>
/// <param name="DueAt">The deadline of the current status.</param>
public sealed record QueueSlaResponse(SlaState State, DateTimeOffset DueAt)
{
    /// <summary>The wire form of <paramref name="badge"/>, or null when no SLA runs.</summary>
    public static QueueSlaResponse? From(SlaBadge? badge) => badge is null ? null : new QueueSlaResponse(badge.State, badge.DueAt);
}

/// <summary>One record of <c>GET api/queues/{recordType}</c>.</summary>
/// <param name="Id">The record's PublicId (family <c>records</c>; opens the record detail).</param>
/// <param name="Number">The reference number.</param>
/// <param name="Status">The status value.</param>
/// <param name="StatusKey">The status's wire key (the client builds the i18n key from it).</param>
/// <param name="SubmittedAt">When the record was first submitted.</param>
/// <param name="OwnerDisplayName">The requester's display name.</param>
/// <param name="Assignee">Who holds the record, or null.</param>
/// <param name="Sla">The SLA badge, or null.</param>
public sealed record WorkQueueItemResponse(
    PublicId<QueueRecordIds> Id,
    string Number,
    int Status,
    string StatusKey,
    DateTimeOffset SubmittedAt,
    string OwnerDisplayName,
    QueueAssigneeResponse? Assignee,
    QueueSlaResponse? Sla)
{
    /// <summary>The wire form of <paramref name="entry"/>.</summary>
    public static WorkQueueItemResponse From(WorkQueueEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);
        return new WorkQueueItemResponse(
            PublicId<QueueRecordIds>.From(entry.Id),
            entry.Number,
            entry.Status,
            entry.StatusKey,
            entry.SubmittedAt,
            entry.OwnerDisplayName,
            QueueAssigneeResponse.From(entry.Assignee),
            QueueSlaResponse.From(entry.Sla));
    }
}

/// <summary>The body of <c>GET api/queues/{recordType}</c>.</summary>
/// <param name="Items">The page's records.</param>
/// <param name="Page">The 1-based page.</param>
/// <param name="PageSize">The page size used.</param>
/// <param name="Total">The number of matching records on every page.</param>
public sealed record WorkQueueResponse(IReadOnlyList<WorkQueueItemResponse> Items, int Page, int PageSize, int Total)
{
    /// <summary>The wire form of <paramref name="page"/>.</summary>
    public static WorkQueueResponse From(WorkQueuePage page)
    {
        ArgumentNullException.ThrowIfNull(page);
        return new WorkQueueResponse([.. page.Items.Select(WorkQueueItemResponse.From)], page.Page, page.PageSize, page.Total);
    }
}
