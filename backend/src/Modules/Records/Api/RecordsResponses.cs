using Codelab.Security.PublicIds;
using Starter.Application.Records;
using Starter.Application.Workflow;
using Starter.Domain.Workflow;
using Starter.Modules.Records.Application;

namespace Starter.Modules.Records.Api;

/// <summary>A page of a list endpoint (<c>page</c> is 1-based).</summary>
/// <typeparam name="T">The item shape.</typeparam>
/// <param name="Items">The items.</param>
/// <param name="Page">The page.</param>
/// <param name="PageSize">The page size.</param>
/// <param name="Total">Every match.</param>
public sealed record RecordPageResponse<T>(IReadOnlyList<T> Items, int Page, int PageSize, int Total);

/// <summary>One record in a list (<c>mine</c>; <c>oversight</c> adds the owner).</summary>
/// <param name="Id">The record id (family <c>records</c>).</param>
/// <param name="RecordType">The record type key.</param>
/// <param name="Number">The reference number, or null until submitted.</param>
/// <param name="Status">The status value.</param>
/// <param name="StatusKey">The status key.</param>
/// <param name="CreatedAt">When it was created.</param>
/// <param name="SubmittedAt">When it was first submitted, or null.</param>
/// <param name="UpdatedAt">When it last changed status.</param>
/// <param name="IsTerminal">True when closed.</param>
/// <param name="OwnerDisplayName">The owner's display name (oversight only; null in <c>mine</c>).</param>
public sealed record RecordListItemResponse(
    PublicId<RecordsIds> Id,
    string RecordType,
    string? Number,
    int Status,
    string StatusKey,
    DateTimeOffset CreatedAt,
    DateTimeOffset? SubmittedAt,
    DateTimeOffset UpdatedAt,
    bool IsTerminal,
    string? OwnerDisplayName)
{
    /// <summary>The wire page of <paramref name="page"/>.</summary>
    public static RecordPageResponse<RecordListItemResponse> PageFrom(RecordListPage page)
    {
        ArgumentNullException.ThrowIfNull(page);
        return new RecordPageResponse<RecordListItemResponse>([.. page.Items.Select(From)], page.Page, page.PageSize, page.Total);
    }

    private static RecordListItemResponse From(RecordListItemView item) => new(
        PublicId<RecordsIds>.From(item.Record.Id),
        item.Record.RecordType,
        item.Record.Number,
        item.Record.Status,
        item.StatusKey,
        CreatedAt: item.Record.CreatedAt,
        SubmittedAt: item.Record.SubmittedAt,
        UpdatedAt: item.Record.UpdatedAt,
        item.IsTerminal,
        item.OwnerDisplayName);
}

/// <summary>The status wording keys of one status, one per audience.</summary>
/// <param name="RequesterKey">The requester screens' key.</param>
/// <param name="StaffKey">The staff screens' key.</param>
/// <param name="AdminKey">The oversight key.</param>
public sealed record StatusWordingResponse(string RequesterKey, string StaffKey, string AdminKey);

/// <summary>One action the caller may take now.</summary>
/// <param name="ActionKey">The key to send back.</param>
/// <param name="LabelKey">The button label key.</param>
/// <param name="Reason">Whether a reason is asked (1 none, 2 optional, 3 required).</param>
/// <param name="ReasonFieldKey">The reason's field, or null.</param>
/// <param name="ToStatus">The status the record lands on.</param>
/// <param name="ToTerminal">True when the action closes the record.</param>
public sealed record AllowedActionResponse(
    string ActionKey, string LabelKey, ReasonRequirement Reason, string? ReasonFieldKey, int ToStatus, bool ToTerminal)
{
    /// <summary>The wire form of <paramref name="action"/>.</summary>
    public static AllowedActionResponse From(AllowedAction action)
    {
        ArgumentNullException.ThrowIfNull(action);
        return new AllowedActionResponse(action.ActionKey, action.LabelKey, action.Reason, action.ReasonFieldKey, action.ToStatus, action.ToTerminal);
    }
}

/// <summary>Who currently works on the record.</summary>
/// <param name="UserId">The assignee (family <c>users</c>).</param>
/// <param name="DisplayName">The assignee's name.</param>
/// <param name="AssignedAt">Since when.</param>
public sealed record RecordAssigneeResponse(PublicId<RecordUserIds> UserId, string DisplayName, DateTimeOffset AssignedAt);

/// <summary>The record's deadline badge.</summary>
/// <param name="State">1 on track, 2 due soon, 3 breached.</param>
/// <param name="DueAt">When the current status is due.</param>
public sealed record RecordSlaResponse(SlaState State, DateTimeOffset DueAt);

/// <summary>The body of <c>GET api/records/{recordType}/{id}</c> and of a transition.</summary>
/// <param name="Id">The record id (family <c>records</c>).</param>
/// <param name="RecordType">The record type key.</param>
/// <param name="Number">The reference number, or null.</param>
/// <param name="Status">The status value.</param>
/// <param name="StatusKey">The status key.</param>
/// <param name="Wording">The status wording keys of every audience.</param>
/// <param name="Audience">How the caller sees the record (1 requester, 2 staff, 3 admin).</param>
/// <param name="IsTerminal">True when closed.</param>
/// <param name="OwnerDisplayName">The owner's display name.</param>
/// <param name="CreatedAt">When it was created.</param>
/// <param name="SubmittedAt">When first submitted, or null.</param>
/// <param name="UpdatedAt">When it last changed status.</param>
/// <param name="LastReason">The reason of the latest timeline entry, or null.</param>
/// <param name="AllowedActions">What the caller may do now (empty when terminal and for oversight).</param>
/// <param name="Assignee">The current assignee, or null.</param>
/// <param name="Sla">The SLA badge, or null.</param>
/// <param name="AttachableRecordType">The record type of its attachment routes.</param>
/// <param name="AttachmentsRecordId">The record id in the attachment routes' family (<c>attachable-records</c>).</param>
public sealed record RecordDetailResponse(
    PublicId<RecordsIds> Id,
    string RecordType,
    string? Number,
    int Status,
    string StatusKey,
    StatusWordingResponse Wording,
    WorkflowAudience Audience,
    bool IsTerminal,
    string OwnerDisplayName,
    DateTimeOffset CreatedAt,
    DateTimeOffset? SubmittedAt,
    DateTimeOffset UpdatedAt,
    string? LastReason,
    IReadOnlyList<AllowedActionResponse> AllowedActions,
    RecordAssigneeResponse? Assignee,
    RecordSlaResponse? Sla,
    string AttachableRecordType,
    PublicId<RecordAttachmentIds> AttachmentsRecordId)
{
    /// <summary>The wire form of <paramref name="view"/>.</summary>
    public static RecordDetailResponse From(RecordDetailView view)
    {
        ArgumentNullException.ThrowIfNull(view);
        var record = view.Record;
        var status = view.Status;
        var key = view.Type.StatusKeyOf(record.Status);
        var wording = status?.Wording ?? new StatusWording(RequesterKey: key, StaffKey: key, AdminKey: key);
        return new RecordDetailResponse(
            PublicId<RecordsIds>.From(record.Id),
            record.RecordType,
            record.Number,
            record.Status,
            key,
            new StatusWordingResponse(wording.RequesterKey, wording.StaffKey, wording.AdminKey),
            view.Audience,
            IsTerminal: status?.IsTerminal == true,
            view.OwnerDisplayName,
            CreatedAt: record.CreatedAt,
            SubmittedAt: record.SubmittedAt,
            UpdatedAt: record.UpdatedAt,
            view.LastReason,
            [.. view.AllowedActions.Select(AllowedActionResponse.From)],
            view.Enrichment.Assignee is { } assignee
                ? new RecordAssigneeResponse(PublicId<RecordUserIds>.From(assignee.AssigneeUserId), assignee.AssigneeDisplayName, assignee.AssignedAt)
                : null,
            view.Enrichment.Sla is { } sla ? new RecordSlaResponse(sla.State, sla.DueAt) : null,
            view.Type.Registration.AttachableRecordType,
            PublicId<RecordAttachmentIds>.From(record.Id));
    }
}

/// <summary>One timeline entry.</summary>
/// <param name="At">When it happened.</param>
/// <param name="ActorDisplayName">The actor's name at the time.</param>
/// <param name="Side">1 requester, 2 staff, 3 system.</param>
/// <param name="ActionKey">The action key.</param>
/// <param name="FromStatus">The status before, or null for the first entry.</param>
/// <param name="ToStatus">The status after.</param>
/// <param name="ToStatusKey">The key of the status after.</param>
/// <param name="Reason">The reason, or null.</param>
public sealed record TimelineEntryResponse(
    DateTimeOffset At,
    string ActorDisplayName,
    TransitionSide Side,
    string ActionKey,
    int? FromStatus,
    int ToStatus,
    string ToStatusKey,
    string? Reason)
{
    /// <summary>The wire list of <paramref name="entries"/>, oldest first.</summary>
    public static IReadOnlyList<TimelineEntryResponse> ListFrom(IReadOnlyList<TimelineEntryView> entries)
    {
        ArgumentNullException.ThrowIfNull(entries);
        return [.. entries.Select(view => new TimelineEntryResponse(
            view.Entry.At,
            view.Entry.ActorName,
            view.Entry.Side,
            view.Entry.Action,
            FromStatus: view.Entry.FromStatus,
            ToStatus: view.Entry.ToStatus ?? view.Entry.FromStatus ?? 0,
            view.ToStatusKey,
            view.Entry.Reason))];
    }
}

/// <summary>
/// The query string of the list endpoints (<c>?status=&amp;number=&amp;page=&amp;pageSize=</c>); every member is optional.
/// <c>number</c> is read by the oversight list only.
/// </summary>
public sealed class RecordListParameters
{
    /// <summary>Only this status, or null.</summary>
    [Microsoft.AspNetCore.Mvc.FromQuery(Name = RecordsRoutes.StatusQuery)]
    public int? Status { get; init; }

    /// <summary>Only numbers containing this text, or null (oversight).</summary>
    [Microsoft.AspNetCore.Mvc.FromQuery(Name = RecordsRoutes.NumberQuery)]
    public string? Number { get; init; }

    /// <summary>The 1-based page, or null for the first.</summary>
    [Microsoft.AspNetCore.Mvc.FromQuery(Name = RecordsRoutes.PageQuery)]
    public int? Page { get; init; }

    /// <summary>The page size, or null for the default.</summary>
    [Microsoft.AspNetCore.Mvc.FromQuery(Name = RecordsRoutes.PageSizeQuery)]
    public int? PageSize { get; init; }
}
