namespace Starter.Application.Records;

/// <summary>The current assignee of one record.</summary>
/// <param name="AssigneeUserId">The staff user the record is assigned to.</param>
/// <param name="AssigneeDisplayName">The assignee's display name.</param>
/// <param name="AssignedAt">When the current assignment started.</param>
public sealed record AssignmentSnapshot(long AssigneeUserId, string AssigneeDisplayName, DateTimeOffset AssignedAt);

/// <summary>
/// The current assignments of records, implemented by the Assignments module and read by the record detail views.
/// Optional: when no implementation is registered, consumers resolve <c>IEnumerable&lt;IRecordAssignmentReader&gt;</c>
/// and show no assignee.
/// </summary>
public interface IRecordAssignmentReader
{
    /// <summary>The current assignment of each of <paramref name="recordIds"/> that has one (unassigned records are absent).</summary>
    Task<IReadOnlyDictionary<long, AssignmentSnapshot>> CurrentAsync(IReadOnlyCollection<long> recordIds, CancellationToken cancellationToken);
}
