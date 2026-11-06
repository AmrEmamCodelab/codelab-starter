using Starter.Application.Records;
using Starter.Modules.Deadlines.Domain;

namespace Starter.Modules.Deadlines.Application;

/// <summary>
/// Starts and stops the SLA timers on every committed record transition: the open deadline of a record closes when the
/// record leaves its status, and a deadline opens when the record enters a non-terminal status with an SLA
/// (<see cref="SlaPolicyCatalog"/>). Idempotent: a replayed transition finds the deadline of its status already open
/// (or nothing to close) and writes nothing.
/// </summary>
public sealed class DeadlineTransitionObserver(SlaPolicyCatalog policies, IRecordDeadlineStore deadlines) : IRecordTransitionObserver
{
    /// <inheritdoc />
    public async Task OnTransitionedAsync(RecordTransitioned transition, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(transition);
        var open = await deadlines.FindOpenAsync(transition.RecordId, transition.RecordType, cancellationToken);
        if (open is not null && open.Status == transition.ToStatus && !transition.ToTerminal)
        {
            return;
        }

        if (open is not null)
        {
            open.Close(transition.At);

            // Saved on its own, so the one-open-deadline-per-record index is free before the next deadline is inserted.
            await deadlines.SaveChangesAsync(cancellationToken);
        }

        if (!transition.ToTerminal && policies.Find(transition.RecordType, transition.ToStatus) is { } definition)
        {
            deadlines.Add(RecordDeadline.Open(transition.RecordId, definition, startedAt: transition.At));
            await deadlines.SaveChangesAsync(cancellationToken);
        }
    }
}
