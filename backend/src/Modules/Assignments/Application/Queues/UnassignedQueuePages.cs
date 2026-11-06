using Starter.Application.Records;
using Starter.Modules.Assignments.Domain;

namespace Starter.Modules.Assignments.Application.Queues;

/// <summary>
/// The unassigned view: the directory's matching records minus the ones with an open assignment, paged in the
/// directory's order. The kernel <see cref="RecordQuery"/> can include ids but not exclude them, so the total is
/// "all matching" minus "assigned and matching", and the page is filled by reading the directory in
/// <see cref="RecordsLimits.MaxPageSize"/> pages and skipping assigned ids (at most
/// <see cref="AssignmentsLimits.UnassignedScanMaxPages"/> pages). Open assignments exist only for open records, so the
/// skipped set stays as small as the staff's current workload.
/// </summary>
public sealed class UnassignedQueuePages(IRecordDirectory directory, IAssignmentStore store)
{
    private const int CountOnlyPageSize = 1;

    /// <summary>The page of <paramref name="query"/> (its page and size) without the assigned records.</summary>
    public async Task<PagedResult<RecordSummary>> QueryAsync(RecordQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        var assigned = await store.OpenRecordIdsAsync(new OpenAssignmentFilter(query.RecordType, AssigneeUserId: null), cancellationToken);
        if (assigned.Count == 0)
        {
            return await directory.QueryAsync(query, cancellationToken);
        }

        var matching = await CountAsync(Paged(query, ids: query.Ids, page: 1, pageSize: CountOnlyPageSize), cancellationToken);
        var matchingAssigned = await CountAsync(Paged(query, ids: assigned, page: 1, pageSize: CountOnlyPageSize), cancellationToken);
        var items = await ScanAsync(query, assigned.ToHashSet(), cancellationToken);
        return new PagedResult<RecordSummary>(items, query.Page, query.PageSize, Math.Max(matching - matchingAssigned, 0));
    }

    /// <summary>A copy of <paramref name="query"/> with other ids and paging (the kernel query's paging is get-only).</summary>
    internal static RecordQuery Paged(RecordQuery query, IReadOnlyCollection<long>? ids, int page, int pageSize) =>
        new(
            RecordType: query.RecordType,
            Statuses: query.Statuses,
            NumberContains: query.NumberContains,
            OrganizationId: query.OrganizationId,
            SubmittedFrom: query.SubmittedFrom,
            SubmittedTo: query.SubmittedTo,
            Ids: ids,
            Page: page,
            PageSize: pageSize);

    private async Task<int> CountAsync(RecordQuery query, CancellationToken cancellationToken) =>
        (await directory.QueryAsync(query, cancellationToken)).Total;

    private async Task<IReadOnlyList<RecordSummary>> ScanAsync(RecordQuery query, HashSet<long> assigned, CancellationToken cancellationToken)
    {
        var skip = (query.Page - 1) * query.PageSize;
        var items = new List<RecordSummary>(query.PageSize);
        for (var scanPage = 1; scanPage <= AssignmentsLimits.UnassignedScanMaxPages && items.Count < query.PageSize; scanPage++)
        {
            var batch = await directory.QueryAsync(Paged(query, ids: query.Ids, page: scanPage, pageSize: RecordsLimits.MaxPageSize), cancellationToken);
            foreach (var record in batch.Items.Where(record => !assigned.Contains(record.Id)))
            {
                if (skip > 0)
                {
                    skip--;
                }
                else if (items.Count < query.PageSize)
                {
                    items.Add(record);
                }
            }

            if (batch.Items.Count < RecordsLimits.MaxPageSize)
            {
                break;
            }
        }

        return items;
    }
}
