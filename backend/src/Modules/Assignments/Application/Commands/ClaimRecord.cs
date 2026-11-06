using Starter.Application.Results;
using Starter.Modules.Assignments.Application.Queues;

namespace Starter.Modules.Assignments.Application.Commands;

/// <summary>
/// The caller takes an open record of a queue they may see. A record somebody already holds (the caller included) is
/// refused with 409 <see cref="AssignmentsErrorCodes.AlreadyClaimed"/>; two claims at the same moment are decided by
/// the database (one open row per record), and the loser gets the same 409.
/// </summary>
public sealed class ClaimRecord(CurrentStaff currentStaff, RecordQueues queues, AssignmentRows rows)
{
    /// <summary>Done; 403, 404, 409 <see cref="AssignmentsErrorCodes.NotAssignable"/> or <see cref="AssignmentsErrorCodes.AlreadyClaimed"/>.</summary>
    public async Task<Outcome<Done>> ExecuteAsync(QueueItemKey item, CancellationToken cancellationToken)
    {
        var caller = await currentStaff.UserIdAsync(cancellationToken);
        if (!caller.IsSuccess)
        {
            return caller.Refusal;
        }

        var found = await queues.FindOpenAsync(item, cancellationToken);
        if (!found.IsSuccess)
        {
            return found.Refusal;
        }

        if (await rows.FindOpenAsync(found.Value.Record.Id, cancellationToken) is not null)
        {
            return Refusal.Conflict(AssignmentsErrorCodes.AlreadyClaimed);
        }

        var parties = new AssignmentParties(AssigneeUserId: caller.Value, ActorUserId: caller.Value);
        return await rows.OpenAsync(found.Value.Ref, parties, cancellationToken)
            ? Done.Value
            : Refusal.Conflict(AssignmentsErrorCodes.AlreadyClaimed);
    }
}
