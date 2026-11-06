using Starter.Domain.Records;

namespace Starter.Modules.Assignments.Domain;

/// <summary>Who starts holding a record, by whom and when.</summary>
/// <param name="Record">The record (id and record type).</param>
/// <param name="AssigneeUserId">The user who will hold the record.</param>
/// <param name="AssignedByUserId">The user who made the assignment (the assignee for a claim).</param>
/// <param name="At">When the assignment starts.</param>
public sealed record AssignmentStart(RecordRef Record, long AssigneeUserId, long AssignedByUserId, DateTimeOffset At);

/// <summary>Who closes an assignment, why and when.</summary>
/// <param name="Reason">Why the row closes.</param>
/// <param name="ReleasedByUserId">The user whose action closed it (for a terminal close, the transition's actor).</param>
/// <param name="At">When it closes.</param>
public sealed record AssignmentEnd(AssignmentReleaseReason Reason, long ReleasedByUserId, DateTimeOffset At);

/// <summary>
/// One row of a record's assignment history (<c>assignments.RecordAssignments</c>): a user held the record from
/// <see cref="AssignedAt"/> until <see cref="ReleasedAt"/>. The current assignee is the one OPEN row (no
/// <see cref="ReleasedAt"/>); the database allows at most one open row per record (filtered unique index). Rows are
/// never deleted or reopened: a new holder means a new row. The record itself carries no assignment column.
/// </summary>
public sealed class RecordAssignment
{
    private RecordAssignment(string recordType)
    {
        RecordType = recordType;
    }

    /// <summary>The row id.</summary>
    public long Id { get; private set; }

    /// <summary>The record type key of the record (the queue the row belongs to).</summary>
    public string RecordType { get; private set; }

    /// <summary>The internal record id (Records module; no cross-schema foreign key).</summary>
    public long RecordId { get; private set; }

    /// <summary>The user who holds (or held) the record.</summary>
    public long AssigneeUserId { get; private set; }

    /// <summary>The user who made the assignment; equal to <see cref="AssigneeUserId"/> for a claim.</summary>
    public long AssignedByUserId { get; private set; }

    /// <summary>When the assignment started.</summary>
    public DateTimeOffset AssignedAt { get; private set; }

    /// <summary>When the assignment ended, or null while it is open.</summary>
    public DateTimeOffset? ReleasedAt { get; private set; }

    /// <summary>Why it ended, or null while it is open.</summary>
    public AssignmentReleaseReason? ReleaseReason { get; private set; }

    /// <summary>The user whose action ended it, or null while it is open.</summary>
    public long? ReleasedByUserId { get; private set; }

    /// <summary>True while the row is the record's current assignment.</summary>
    public bool IsOpen => ReleasedAt is null;

    /// <summary>A new open row.</summary>
    /// <exception cref="ArgumentNullException"><paramref name="start"/> is null.</exception>
    /// <exception cref="ArgumentOutOfRangeException">A user id is below 1.</exception>
    public static RecordAssignment Open(AssignmentStart start)
    {
        ArgumentNullException.ThrowIfNull(start);
        ArgumentOutOfRangeException.ThrowIfLessThan(start.AssigneeUserId, 1, nameof(start));
        ArgumentOutOfRangeException.ThrowIfLessThan(start.AssignedByUserId, 1, nameof(start));
        return new RecordAssignment(start.Record.RecordType)
        {
            RecordId = start.Record.Id,
            AssigneeUserId = start.AssigneeUserId,
            AssignedByUserId = start.AssignedByUserId,
            AssignedAt = start.At,
        };
    }

    /// <summary>
    /// Closes the row. An end earlier than <see cref="AssignedAt"/> (clock skew between nodes) is recorded at
    /// <see cref="AssignedAt"/>, so a row never ends before it starts.
    /// </summary>
    /// <exception cref="InvalidOperationException">The row is already closed.</exception>
    /// <exception cref="ArgumentOutOfRangeException">The reason is not a defined member.</exception>
    public void Release(AssignmentEnd end)
    {
        ArgumentNullException.ThrowIfNull(end);
        if (!Enum.IsDefined(end.Reason))
        {
            throw new ArgumentOutOfRangeException(nameof(end), (int)end.Reason, "Not a defined release reason.");
        }

        if (!IsOpen)
        {
            throw new InvalidOperationException($"Assignment {Id} of record {RecordId} is already closed.");
        }

        ReleasedAt = end.At < AssignedAt ? AssignedAt : end.At;
        ReleaseReason = end.Reason;
        ReleasedByUserId = end.ReleasedByUserId;
    }
}
