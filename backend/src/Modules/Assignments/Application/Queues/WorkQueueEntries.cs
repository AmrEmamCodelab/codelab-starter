using Starter.Application.Records;

namespace Starter.Modules.Assignments.Application.Queues;

/// <summary>
/// Turns a directory page into work queue entries: status keys from the queue's workflow, the current assignee
/// (<see cref="RecordAssignmentReader"/>), the owner's display name and the SLA badge of every registered
/// <see cref="IRecordSlaReader"/> (none registered: no badge).
/// </summary>
public sealed class WorkQueueEntries(RecordAssignmentReader assignments, UserDisplayNames displayNames, IEnumerable<IRecordSlaReader> slaReaders)
{
    /// <summary>The entries of <paramref name="records"/>, in their order.</summary>
    public async Task<IReadOnlyList<WorkQueueEntry>> BuildAsync(RecordQueueContext queue, IReadOnlyList<RecordSummary> records, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(queue);
        ArgumentNullException.ThrowIfNull(records);
        if (records.Count == 0)
        {
            return [];
        }

        var ids = records.Select(record => record.Id).ToList();
        var current = await assignments.CurrentAsync(ids, cancellationToken);
        var badges = await BadgesAsync(ids, cancellationToken);
        var owners = await displayNames.ForAsync(records.Select(record => record.OwnerUserId), cancellationToken);
        return [.. records.Select(record => new WorkQueueEntry(
            Id: record.Id,
            Number: record.Number ?? string.Empty,
            Status: record.Status,
            StatusKey: queue.StatusKey(record.Status),
            SubmittedAt: record.SubmittedAt ?? record.CreatedAt,
            OwnerDisplayName: owners[record.OwnerUserId],
            Assignee: current.GetValueOrDefault(record.Id),
            Sla: badges.GetValueOrDefault(record.Id)))];
    }

    private async Task<Dictionary<long, SlaBadge>> BadgesAsync(IReadOnlyCollection<long> ids, CancellationToken cancellationToken)
    {
        var badges = new Dictionary<long, SlaBadge>();
        foreach (var reader in slaReaders)
        {
            foreach (var (recordId, badge) in await reader.BadgesAsync(ids, cancellationToken))
            {
                badges.TryAdd(recordId, badge);
            }
        }

        return badges;
    }
}
