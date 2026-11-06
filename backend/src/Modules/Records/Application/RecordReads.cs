using Starter.Application.Records;
using Starter.Application.Results;
using Starter.Application.Workflow;
using Starter.Domain.Workflow;

namespace Starter.Modules.Records.Application;

/// <summary>
/// <c>GET api/records/{recordType}/{id}</c>: one record for the caller's audience, with the actions the caller may take
/// now computed by the workflow engine (never a hand-written matrix): requester actions for the owner, staff actions for
/// permitted staff, none for oversight and none on a terminal record.
/// </summary>
public sealed class GetRecordDetail(RecordLoader loader, IRecordStore store, RecordUserNames names, RecordEnrichment enrichment)
{
    /// <summary>The detail of record <paramref name="recordId"/> of <paramref name="recordType"/>, or 403/404.</summary>
    public async Task<Outcome<RecordDetailView>> ExecuteAsync(string? recordType, long recordId, CancellationToken cancellationToken)
    {
        var load = await loader.LoadAsync(recordType, recordId, cancellationToken);
        return load.IsSuccess ? await DetailOfAsync(load.Value, cancellationToken) : load.Refusal;
    }

    /// <summary>The detail of an already loaded record (after a transition, so the answer shows the new state).</summary>
    public async Task<RecordDetailView> DetailOfAsync(LoadedRecord loaded, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(loaded);
        var record = loaded.Record;
        var latest = await store.LatestHistoryAsync(record.Id, cancellationToken);
        var owner = await names.ForAsync([record.OwnerUserId], cancellationToken);
        return new RecordDetailView(
            record,
            loaded.Type,
            loaded.Standing.Audience!.Value,
            OwnerDisplayName: owner[record.OwnerUserId],
            LastReason: latest?.Reason,
            AllowedActions: AllowedActionsOf(loaded),
            Enrichment: await enrichment.ForAsync(record.Id, cancellationToken));
    }

    /// <summary>The caller's actions now: none for the admin audience, otherwise the engine's for the caller's side.</summary>
    public static IReadOnlyList<AllowedAction> AllowedActionsOf(LoadedRecord loaded)
    {
        ArgumentNullException.ThrowIfNull(loaded);
        var standing = loaded.Standing;
        return standing.Audience is WorkflowAudience.Admin
            ? []
            : RecordWorkflowEngine.AllowedActions(loaded.Type.Catalog, loaded.Record.Status, standing.Holds, isOwner: standing.IsOwner, standing.Side);
    }
}

/// <summary><c>GET api/records/{recordType}/{id}/timeline</c>: every timeline entry, oldest first, for whoever may see the record.</summary>
public sealed class GetRecordTimeline(RecordLoader loader, IRecordStore store)
{
    /// <summary>The timeline of record <paramref name="recordId"/> of <paramref name="recordType"/>, or 403/404.</summary>
    public async Task<Outcome<IReadOnlyList<TimelineEntryView>>> ExecuteAsync(string? recordType, long recordId, CancellationToken cancellationToken)
    {
        var load = await loader.LoadAsync(recordType, recordId, cancellationToken);
        if (!load.IsSuccess)
        {
            return load.Refusal;
        }

        var history = await store.HistoryAsync(recordId, cancellationToken);
        return Outcome.Success<IReadOnlyList<TimelineEntryView>>(
            [.. history.Select(entry => new TimelineEntryView(entry, ToStatusKey: load.Value.Type.StatusKeyOf(entry.ToStatus ?? load.Value.Record.Status)))]);
    }
}

/// <summary>The query of <c>GET api/records/{recordType}/mine</c>.</summary>
/// <param name="RecordType">The record type from the route.</param>
/// <param name="Status">Only this status, or null.</param>
/// <param name="Page">The 1-based page, or null for the first.</param>
/// <param name="PageSize">The page size, or null for the default (capped at <see cref="RecordsLimits.MaxPageSize"/>).</param>
public sealed record MyRecordsQuery(string? RecordType, int? Status, int? Page, int? PageSize);

/// <summary>
/// <c>GET api/records/{recordType}/mine</c>: the caller's own records of one type (drafts included), newest first; needs
/// the type's view-own permission (403 <see cref="RecordsModuleErrorCodes.ViewForbidden"/>).
/// </summary>
public sealed class ListMyRecords(RecordTypes types, RecordCallers callers, RecordStandings standings, IRecordStore store)
{
    /// <summary>The caller's page.</summary>
    public async Task<Outcome<RecordListPage>> ExecuteAsync(MyRecordsQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        var type = types.Find(query.RecordType);
        if (type is null)
        {
            return Refusal.NotFound(RecordsErrorCodes.NotFound);
        }

        var caller = await callers.CurrentAsync(cancellationToken);
        if (caller is null)
        {
            return Refusal.Forbidden(RecordsModuleErrorCodes.ActorUnknown);
        }

        if (!(await standings.ScopeForAsync(type.Registration.ViewOwnPermissionKey, cancellationToken)).Allowed)
        {
            return Refusal.Forbidden(RecordsModuleErrorCodes.ViewForbidden);
        }

        var paging = RecordPageRequest.From(query.Page, query.PageSize);
        var filter = new RecordQuery(
            type.RecordType,
            StatusFilter(query.Status),
            NumberContains: null,
            OrganizationId: null,
            SubmittedFrom: null,
            SubmittedTo: null,
            Ids: null,
            Page: paging.Page,
            PageSize: paging.PageSize);
        var page = await store.PageAsync(new RecordPageQuery(filter, OwnerUserId: caller.UserId, SubmittedOnly: false), cancellationToken);
        return RecordLists.PageOf(type, page, ownerNames: null);
    }

    /// <summary>The status filter of a list query: none, or the one status asked for.</summary>
    public static IReadOnlyCollection<int>? StatusFilter(int? status) => status is { } value ? [value] : null;
}

/// <summary>Shapes record list pages (one owner for the status key and the terminal flag of a list item).</summary>
public static class RecordLists
{
    /// <summary>The page of <paramref name="page"/>, with owner names when given.</summary>
    public static RecordListPage PageOf(RegisteredRecordType type, PagedResult<Domain.Record> page, IReadOnlyDictionary<long, string>? ownerNames)
    {
        ArgumentNullException.ThrowIfNull(type);
        ArgumentNullException.ThrowIfNull(page);
        return new RecordListPage(
            [.. page.Items.Select(record => new RecordListItemView(
                record,
                type.StatusKeyOf(record.Status),
                IsTerminal: type.Catalog.IsTerminal(record.Status),
                OwnerDisplayName: ownerNames?.GetValueOrDefault(record.OwnerUserId)))],
            page.Page,
            page.PageSize,
            page.Total);
    }
}
