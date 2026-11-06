using Microsoft.Extensions.Logging;
using Starter.Application.Records;
using Starter.Modules.Assignments.Domain;

namespace Starter.Modules.Assignments.Application.Commands;

/// <summary>
/// The auto-release: when a record reaches a terminal status (the Records module reports it after the commit), its open
/// assignment closes as <see cref="AssignmentReleaseReason.RecordClosed"/> at the transition's time, by the
/// transition's actor. Idempotent: a record without an open row (already closed, never assigned) changes nothing.
/// Non-terminal transitions keep the assignee.
/// </summary>
public sealed class CloseAssignmentOnTerminal(IAssignmentStore store, ILogger<CloseAssignmentOnTerminal> logger) : IRecordTransitionObserver
{
    /// <inheritdoc />
    public async Task OnTransitionedAsync(RecordTransitioned transition, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(transition);
        if (!transition.ToTerminal)
        {
            return;
        }

        var open = await store.FindOpenAsync(transition.RecordId, cancellationToken);
        if (open is null)
        {
            return;
        }

        open.Release(new AssignmentEnd(AssignmentReleaseReason.RecordClosed, ReleasedByUserId: transition.ActorUserId, At: transition.At));
        await store.SaveChangesAsync(cancellationToken);
        AssignmentsLog.ClosedOnTerminal(logger, recordId: transition.RecordId, recordType: transition.RecordType, status: transition.ToStatus);
    }
}
