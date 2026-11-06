using Starter.Domain.Auditing;
using Starter.Domain.Workflow;

namespace Starter.Modules.Records.Domain;

/// <summary>
/// One timeline row of a record (<c>records.RecordHistory</c>): the action, the actor (id and display name at the time),
/// the side the action belongs to, the status before and after and the reason. Append-only, written in the same save as
/// the status change, and mapped ON DELETE RESTRICT (<c>HasRecordHistory</c>): a record with history can never be
/// deleted, so the timeline is never cascaded away.
/// </summary>
public sealed class RecordHistory : RecordHistoryEntry
{
    private RecordHistory()
    {
    }

    /// <summary>A new entry.</summary>
    /// <exception cref="ArgumentException">The facts break a column limit (see <see cref="RecordHistoryLimits"/>).</exception>
    public RecordHistory(RecordHistoryFacts facts, TransitionSide side)
        : base(facts)
    {
        Side = side;
    }

    /// <summary>Which side took the action (requester, staff or the system); stored as its int value.</summary>
    public TransitionSide Side { get; private set; }
}
