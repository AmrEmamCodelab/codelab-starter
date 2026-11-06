using Starter.Application.Records;
using Starter.Domain.Auditing;
using Starter.Domain.Records;

namespace Starter.Modules.Deadlines.Domain;

/// <summary>
/// The SLA timer of one record in one status (<c>deadlines.RecordDeadlines</c>): opened when the record enters a status
/// with an SLA, closed when it leaves it. Its due and due-soon times are frozen when it opens, so changing an SLA
/// definition never moves a running deadline. A breach is marked once (<see cref="BreachedAt"/>) and escalated once
/// (<see cref="EscalatedAt"/>). The table is technical timer state with a high churn; the record history already holds
/// every transition, so it is not audited.
/// </summary>
[NotAudited]
public sealed class RecordDeadline
{
    private RecordDeadline()
    {
    }

    /// <summary>The internal id.</summary>
    public long Id { get; private set; }

    /// <summary>The internal record id.</summary>
    public long RecordId { get; private set; }

    /// <summary>The record type key.</summary>
    public string RecordType { get; private set; } = string.Empty;

    /// <summary>The status value the deadline runs in.</summary>
    public int Status { get; private set; }

    /// <summary>When the record entered <see cref="Status"/>.</summary>
    public DateTimeOffset StartedAt { get; private set; }

    /// <summary>The deadline: from this instant on the record is breached.</summary>
    public DateTimeOffset DueAt { get; private set; }

    /// <summary>From this instant on the record is due soon (equal to <see cref="DueAt"/> without a due-soon window).</summary>
    public DateTimeOffset DueSoonAt { get; private set; }

    /// <summary>When the scan marked the breach, or null.</summary>
    public DateTimeOffset? BreachedAt { get; private set; }

    /// <summary>When the scan claimed the breach's escalation, or null while it waits.</summary>
    public DateTimeOffset? EscalatedAt { get; private set; }

    /// <summary>When the record left <see cref="Status"/>, or null while the deadline runs.</summary>
    public DateTimeOffset? ClosedAt { get; private set; }

    /// <summary>True while the record is still in <see cref="Status"/>.</summary>
    public bool IsOpen => ClosedAt is null;

    /// <summary>
    /// A running deadline of <paramref name="definition"/> for the record with <paramref name="recordId"/>, which entered
    /// the status at <paramref name="startedAt"/>.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">The record id is below 1.</exception>
    public static RecordDeadline Open(long recordId, SlaDefinition definition, DateTimeOffset startedAt)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(recordId, 1);
        ArgumentNullException.ThrowIfNull(definition);
        var dueAt = startedAt + definition.Duration;
        return new RecordDeadline
        {
            RecordId = recordId,
            RecordType = RecordTypeKeyFormat.EnsureValid(definition.RecordType, nameof(definition)),
            Status = definition.Status,
            StartedAt = startedAt,
            DueAt = dueAt,
            DueSoonAt = dueAt - definition.DueSoonBefore,
        };
    }

    /// <summary>The badge at <paramref name="now"/>: breached from <see cref="DueAt"/> on, due soon from <see cref="DueSoonAt"/>, otherwise on track.</summary>
    public SlaBadge BadgeAt(DateTimeOffset now)
    {
        if (now >= DueAt)
        {
            return new SlaBadge(SlaState.Breached, DueAt);
        }

        return new SlaBadge(now >= DueSoonAt ? SlaState.DueSoon : SlaState.OnTrack, DueAt);
    }

    /// <summary>True when the deadline is open, past <see cref="DueAt"/> at <paramref name="now"/> and not yet marked.</summary>
    public bool IsUnmarkedBreachAt(DateTimeOffset now) => IsOpen && BreachedAt is null && now >= DueAt;

    /// <summary>True when the breach is marked, still open and its escalation has not been claimed.</summary>
    public bool AwaitsEscalation => IsOpen && BreachedAt is not null && EscalatedAt is null;

    /// <summary>Marks the breach at <paramref name="at"/>; a deadline that is closed, not yet due or already marked is left as it is.</summary>
    /// <returns>True when the breach was marked by this call.</returns>
    public bool MarkBreached(DateTimeOffset at)
    {
        if (!IsUnmarkedBreachAt(at))
        {
            return false;
        }

        BreachedAt = at;
        return true;
    }

    /// <summary>Claims the breach's one escalation at <paramref name="at"/>.</summary>
    /// <exception cref="InvalidOperationException">The breach is not marked, the deadline is closed, or the escalation was already claimed.</exception>
    public void ClaimEscalation(DateTimeOffset at)
    {
        if (!AwaitsEscalation)
        {
            throw new InvalidOperationException($"Deadline {Id} has no breach waiting for its escalation.");
        }

        EscalatedAt = at;
    }

    /// <summary>Gives a claimed escalation back (it failed before any recipient was notified), so the next scan retries it.</summary>
    public void ReleaseEscalation() => EscalatedAt = null;

    /// <summary>Closes the deadline at <paramref name="at"/> (the record left the status); closing a closed deadline does nothing.</summary>
    public void Close(DateTimeOffset at)
    {
        if (IsOpen)
        {
            ClosedAt = at;
        }
    }
}
