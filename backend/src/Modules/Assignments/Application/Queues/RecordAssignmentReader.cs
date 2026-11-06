using Starter.Application.Records;

namespace Starter.Modules.Assignments.Application.Queues;

/// <summary>
/// The kernel <see cref="IRecordAssignmentReader"/>: the current (open) assignment of each record, with the assignee's
/// display name. The record detail views and the work queue both read assignees through it.
/// </summary>
public sealed class RecordAssignmentReader(IAssignmentStore store, UserDisplayNames displayNames) : IRecordAssignmentReader
{
    /// <inheritdoc />
    public async Task<IReadOnlyDictionary<long, AssignmentSnapshot>> CurrentAsync(IReadOnlyCollection<long> recordIds, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(recordIds);
        if (recordIds.Count == 0)
        {
            return new Dictionary<long, AssignmentSnapshot>();
        }

        var open = await store.ListOpenAsync(recordIds, cancellationToken);
        var names = await displayNames.ForAsync(open.Select(row => row.AssigneeUserId), cancellationToken);
        return open.ToDictionary(
            row => row.RecordId,
            row => new AssignmentSnapshot(row.AssigneeUserId, names[row.AssigneeUserId], row.AssignedAt));
    }
}
