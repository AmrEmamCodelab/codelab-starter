namespace Starter.Domain.Auditing;

/// <summary>
/// The base of every record history table: one append-only row per business event on a record (submitted, approved,
/// returned...), the timeline a requester and staff read. It sits beside the data-level audit trail, never instead of
/// it. Map a subtype with <c>HasRecordHistory&lt;TRecord, THistory&gt;()</c> (Starter.Infrastructure.Auditing), which
/// points <see cref="RecordId"/> at the record with ON DELETE RESTRICT: a record with history can never be deleted,
/// so the timeline cannot be cascaded away. The DBA script denies UPDATE and DELETE on these tables.
/// </summary>
/// <remarks>
/// The row is itself a trail, so it is not audited again (<see cref="NotAuditedAttribute"/>). The actor name and the
/// reason are personal data and are masked wherever the audit would see them.
/// </remarks>
[NotAudited]
public abstract class RecordHistoryEntry
{
    /// <summary>For EF Core materialization.</summary>
    protected RecordHistoryEntry()
    {
    }

    /// <summary>A new entry from <paramref name="facts"/>.</summary>
    /// <exception cref="ArgumentException">The action or the actor name is blank or too long, or the reason is too long.</exception>
    protected RecordHistoryEntry(RecordHistoryFacts facts)
    {
        RecordHistoryLimits.Check(facts);
        RecordId = facts.RecordId;
        Action = facts.Action;
        FromStatus = facts.FromStatus;
        ToStatus = facts.ToStatus;
        Reason = facts.Reason;
        ActorUserId = facts.ActorUserId;
        ActorName = facts.ActorName;
        At = facts.At;
    }

    /// <summary>The row id (identity).</summary>
    public long Id { get; private set; }

    /// <summary>The record this entry belongs to (FK, ON DELETE RESTRICT).</summary>
    public long RecordId { get; private set; }

    /// <summary>The business action key (a transition or event constant of the record's module).</summary>
    public string Action { get; private set; } = string.Empty;

    /// <summary>The status id before the action (a lookup id), or null when there was none.</summary>
    public int? FromStatus { get; private set; }

    /// <summary>The status id after the action (a lookup id), or null when the status did not change.</summary>
    public int? ToStatus { get; private set; }

    /// <summary>The reason the actor gave, or null.</summary>
    [PersonalData(PersonalDataKind.FreeText)]
    public string? Reason { get; private set; }

    /// <summary>The acting user's internal id, or null for the system actor.</summary>
    public long? ActorUserId { get; private set; }

    /// <summary>The actor's display name at the time, kept so the timeline reads the same after a rename.</summary>
    [PersonalData(PersonalDataKind.Name)]
    public string ActorName { get; private set; } = string.Empty;

    /// <summary>When the action happened (from <see cref="TimeProvider"/>).</summary>
    public DateTimeOffset At { get; private set; }
}

/// <summary>The facts of one record history entry (a parameter object, so same-typed values cannot be swapped).</summary>
/// <param name="RecordId">The record's internal id.</param>
/// <param name="Action">The business action key.</param>
/// <param name="FromStatus">The status id before, or null.</param>
/// <param name="ToStatus">The status id after, or null.</param>
/// <param name="Reason">The reason, or null.</param>
/// <param name="ActorUserId">The acting user's internal id, or null for the system actor.</param>
/// <param name="ActorName">The actor's display name.</param>
/// <param name="At">When it happened.</param>
public sealed record RecordHistoryFacts(
    long RecordId,
    string Action,
    int? FromStatus,
    int? ToStatus,
    string? Reason,
    long? ActorUserId,
    string ActorName,
    DateTimeOffset At);

/// <summary>The column limits of a record history table.</summary>
public static class RecordHistoryLimits
{
    /// <summary>The longest action key.</summary>
    public const int ActionMaxLength = 64;

    /// <summary>The longest reason.</summary>
    public const int ReasonMaxLength = 2000;

    /// <summary>The longest actor name.</summary>
    public const int ActorNameMaxLength = 256;

    /// <summary>Refuses facts the columns cannot hold.</summary>
    /// <exception cref="ArgumentException">The action or the actor name is blank or too long, or the reason is too long.</exception>
    public static void Check(RecordHistoryFacts facts)
    {
        ArgumentNullException.ThrowIfNull(facts);
        ArgumentException.ThrowIfNullOrWhiteSpace(facts.Action, nameof(facts));
        ArgumentException.ThrowIfNullOrWhiteSpace(facts.ActorName, nameof(facts));
        RefuseLongerThan(facts.Action, ActionMaxLength, nameof(facts.Action));
        RefuseLongerThan(facts.ActorName, ActorNameMaxLength, nameof(facts.ActorName));
        RefuseLongerThan(facts.Reason, ReasonMaxLength, nameof(facts.Reason));
    }

    private static void RefuseLongerThan(string? value, int maxLength, string field)
    {
        if (value is not null && value.Length > maxLength)
        {
            throw new ArgumentException($"The record history {field} is longer than {maxLength} characters.", field);
        }
    }
}
