using Starter.Application.Results;
using Starter.Modules.Assignments.Application.Queues;
using Starter.Modules.Assignments.Domain;

namespace Starter.Modules.Assignments.Application.Commands;

/// <summary>
/// The caller gives a record they hold back to the queue (the row closes as <see cref="AssignmentReleaseReason.Released"/>).
/// Only the current assignee may release; anyone else, also when nobody holds the record, gets 403
/// <see cref="AssignmentsErrorCodes.NotAssignee"/>. A supervisor moves a record with an assignment instead.
/// </summary>
public sealed class ReleaseRecord(CurrentStaff currentStaff, RecordQueues queues, AssignmentRows rows)
{
    /// <summary>Done; 403 <see cref="AssignmentsErrorCodes.NotAssignee"/>, 403 or 404 for the queue and the record.</summary>
    public async Task<Outcome<Done>> ExecuteAsync(QueueItemKey item, CancellationToken cancellationToken)
    {
        var caller = await currentStaff.UserIdAsync(cancellationToken);
        if (!caller.IsSuccess)
        {
            return caller.Refusal;
        }

        var found = await queues.FindAsync(item, cancellationToken);
        if (!found.IsSuccess)
        {
            return found.Refusal;
        }

        var open = await rows.FindOpenAsync(found.Value.Record.Id, cancellationToken);
        if (open is null || open.AssigneeUserId != caller.Value)
        {
            return Refusal.Forbidden(AssignmentsErrorCodes.NotAssignee);
        }

        await rows.ReleaseAsync(open, AssignmentReleaseReason.Released, actorUserId: caller.Value, cancellationToken);
        return Done.Value;
    }
}
