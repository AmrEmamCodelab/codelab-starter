using Starter.Application.Records;
using Starter.Application.Results;
using Starter.Application.Validation;
using Starter.Modules.Assignments.Domain;

namespace Starter.Modules.Assignments.Application.Queues;

/// <summary>
/// One page of a record type's work queue in a view (unassigned, mine, all), filtered by status, number and submit
/// range, narrowed to the caller's organization scope for the queue permission. Only open records are listed (not
/// drafts, not closed records).
/// </summary>
public sealed class GetWorkQueuePage(
    IValidator<WorkQueueRequest> validator,
    CurrentStaff currentStaff,
    RecordQueues queues,
    WorkQueueViews views,
    WorkQueueEntries entries)
{
    /// <summary>
    /// The page; 400 <c>VALIDATION_FAILED</c> (view, number, range, or a status that is not an open status of the
    /// queue), 403, or 404 for an unknown record type.
    /// </summary>
    public async Task<Outcome<WorkQueuePage>> ExecuteAsync(WorkQueueRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var errors = validator.Validate(request);
        if (!errors.IsValid)
        {
            return Refusal.Invalid(errors);
        }

        var caller = await currentStaff.UserIdAsync(cancellationToken);
        if (!caller.IsSuccess)
        {
            return caller.Refusal;
        }

        var opened = await queues.OpenAsync(request.RecordType, cancellationToken);
        if (!opened.IsSuccess)
        {
            return opened.Refusal;
        }

        return await ListAsync(opened.Value, request, caller.Value, cancellationToken);
    }

    private async Task<Outcome<WorkQueuePage>> ListAsync(RecordQueueContext queue, WorkQueueRequest request, long callerId, CancellationToken cancellationToken)
    {
        var statuses = queue.StatusFilter(request.Status);
        if (statuses is null)
        {
            return Refusal.Invalid(new ValidationErrors().Add(FieldNames.Of(nameof(WorkQueueRequest.Status)), ValidationMessages.InvalidFormat));
        }

        var page = RecordPageRequest.From(request.Page, request.PageSize);
        var organization = queue.Scope.Narrow(requestedOrganizationId: null);
        if (organization.MatchesNothing)
        {
            return WorkQueuePage.Empty(page);
        }

        var query = new RecordQuery(
            RecordType: queue.RecordType,
            Statuses: statuses,
            NumberContains: request.NumberSearch,
            OrganizationId: organization.OrganizationId,
            SubmittedFrom: request.SubmittedFrom,
            SubmittedTo: request.SubmittedTo,
            Ids: null,
            Page: page.Page,
            PageSize: page.PageSize);
        var found = await views.QueryAsync(QueueViews.Parse(request.View)!.Value, query, callerId, cancellationToken);
        if (found is null)
        {
            return WorkQueuePage.Empty(page);
        }

        var items = await entries.BuildAsync(queue, found.Items, cancellationToken);
        return new WorkQueuePage(items, Page: found.Page, PageSize: found.PageSize, Total: found.Total);
    }
}

/// <summary>The directory page of each queue view (the one owner of what a view lists).</summary>
public sealed class WorkQueueViews(IRecordDirectory directory, IAssignmentStore store, UnassignedQueuePages unassignedPages)
{
    /// <summary>
    /// The page of <paramref name="view"/> for <paramref name="query"/>: unassigned records, the records
    /// <paramref name="callerId"/> holds, or every record; null when the view holds nothing (nothing is assigned to the caller).
    /// </summary>
    public async Task<PagedResult<RecordSummary>?> QueryAsync(QueueView view, RecordQuery query, long callerId, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        switch (view)
        {
            case QueueView.Unassigned:
                return await unassignedPages.QueryAsync(query, cancellationToken);
            case QueueView.Mine:
                var mine = await store.OpenRecordIdsAsync(new OpenAssignmentFilter(query.RecordType, AssigneeUserId: callerId), cancellationToken);
                return mine.Count == 0
                    ? null
                    : await directory.QueryAsync(UnassignedQueuePages.Paged(query, ids: mine, page: query.Page, pageSize: query.PageSize), cancellationToken);
            case QueueView.All:
                return await directory.QueryAsync(query, cancellationToken);
            default:
                throw new ArgumentOutOfRangeException(nameof(view), (int)view, "Not a queue view.");
        }
    }
}
