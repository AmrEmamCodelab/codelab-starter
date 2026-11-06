using Microsoft.Extensions.Logging;
using Starter.Domain.Records;
using Starter.Modules.Assignments.Domain;

namespace Starter.Modules.Assignments.Application.Commands;

/// <summary>Who takes a record and who acts (equal for a claim).</summary>
/// <param name="AssigneeUserId">The user who will hold the record.</param>
/// <param name="ActorUserId">The user who acts.</param>
public sealed record AssignmentParties(long AssigneeUserId, long ActorUserId);

/// <summary>
/// The one writer of assignment rows: opens, releases and replaces them at the <see cref="TimeProvider"/> time, saves,
/// and logs every committed change. The use cases decide WHETHER; this class owns HOW a row changes.
/// </summary>
public sealed class AssignmentRows(IAssignmentStore store, TimeProvider timeProvider, ILogger<AssignmentRows> logger)
{
    /// <summary>The open row of <paramref name="recordId"/> (tracked), or null when nobody holds the record.</summary>
    public Task<RecordAssignment?> FindOpenAsync(long recordId, CancellationToken cancellationToken) =>
        store.FindOpenAsync(recordId, cancellationToken);

    /// <summary>Opens a row for <paramref name="record"/>; false when another open row won the race (nothing written).</summary>
    public async Task<bool> OpenAsync(RecordRef record, AssignmentParties parties, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(parties);
        store.Add(RecordAssignment.Open(Start(record, parties)));
        if (!await store.TrySaveAsync(cancellationToken))
        {
            return false;
        }

        LogOpened(record, parties);
        return true;
    }

    /// <summary>Closes <paramref name="open"/> for <paramref name="reason"/> by <paramref name="actorUserId"/>, now, and saves.</summary>
    public async Task ReleaseAsync(RecordAssignment open, AssignmentReleaseReason reason, long actorUserId, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(open);
        open.Release(new AssignmentEnd(reason, ReleasedByUserId: actorUserId, At: timeProvider.GetUtcNow()));
        await store.SaveChangesAsync(cancellationToken);
        AssignmentsLog.Released(logger, recordId: open.RecordId, recordType: open.RecordType, userId: actorUserId);
    }

    /// <summary>
    /// Closes <paramref name="open"/> as <see cref="AssignmentReleaseReason.Reassigned"/> and opens the new holder's row,
    /// in one transaction; false when another open row won the race (nothing written).
    /// </summary>
    public async Task<bool> ReassignAsync(RecordAssignment open, AssignmentParties parties, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(open);
        ArgumentNullException.ThrowIfNull(parties);
        var record = new RecordRef(open.RecordId, open.RecordType);
        open.Release(new AssignmentEnd(AssignmentReleaseReason.Reassigned, ReleasedByUserId: parties.ActorUserId, At: timeProvider.GetUtcNow()));
        if (!await store.TryReassignAsync(RecordAssignment.Open(Start(record, parties)), cancellationToken))
        {
            return false;
        }

        LogOpened(record, parties);
        return true;
    }

    private AssignmentStart Start(RecordRef record, AssignmentParties parties) =>
        new(record, AssigneeUserId: parties.AssigneeUserId, AssignedByUserId: parties.ActorUserId, At: timeProvider.GetUtcNow());

    private void LogOpened(RecordRef record, AssignmentParties parties)
    {
        if (parties.AssigneeUserId == parties.ActorUserId)
        {
            AssignmentsLog.Claimed(logger, recordId: record.Id, recordType: record.RecordType, userId: parties.ActorUserId);
        }
        else
        {
            AssignmentsLog.Assigned(logger, recordId: record.Id, recordType: record.RecordType, assigneeUserId: parties.AssigneeUserId, userId: parties.ActorUserId);
        }
    }
}
