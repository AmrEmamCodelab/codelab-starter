using Codelab.Security.PublicIds;
using Starter.Application.Results;
using Starter.Application.Validation;
using Starter.Modules.Assignments.Application.Queues;

namespace Starter.Modules.Assignments.Application.Commands;

/// <summary>The body of <c>POST api/queues/{recordType}/{id}/assign</c>, validated by <see cref="AssignRecordValidator"/>.</summary>
/// <param name="UserId">The PublicId of the user who will hold the record.</param>
public sealed record AssignRecordCommand([property: WireId] PublicId<AssigneeUserIds> UserId);

/// <summary>Validates <see cref="AssignRecordCommand"/>: the user id is required. Never throws.</summary>
public sealed class AssignRecordValidator : IValidator<AssignRecordCommand>
{
    /// <inheritdoc />
    public ValidationErrors Validate(AssignRecordCommand input)
    {
        ArgumentNullException.ThrowIfNull(input);
        var errors = new ValidationErrors();
        return input.UserId.HasValue ? errors : errors.Add(FieldNames.Of(nameof(AssignRecordCommand.UserId)), ValidationMessages.Required);
    }
}

/// <summary>
/// A supervisor hands an open record to a user who holds the record type's queue permission for the record's
/// organization (<see cref="IAssigneeEligibility"/>). A record somebody holds is moved: their row closes as
/// <c>Reassigned</c> and the new holder's row opens in the same transaction. Assigning the current holder again changes
/// nothing. An ineligible user (no queue permission, the wrong organization, inactive, unknown) is 400
/// <see cref="AssignmentsErrorCodes.AssigneeNotEligible"/>.
/// </summary>
public sealed class AssignRecord(
    IValidator<AssignRecordCommand> validator,
    CurrentStaff currentStaff,
    RecordQueues queues,
    IAssigneeEligibility eligibility,
    AssignmentRows rows)
{
    /// <summary>Done; 400, 403, 404, 409 <see cref="AssignmentsErrorCodes.NotAssignable"/> or <see cref="AssignmentsErrorCodes.AlreadyClaimed"/> (a claim won the race).</summary>
    public async Task<Outcome<Done>> ExecuteAsync(QueueItemKey item, AssignRecordCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        var errors = validator.Validate(command);
        if (!errors.IsValid)
        {
            return Refusal.Invalid(errors);
        }

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

        var parties = new AssignmentParties(AssigneeUserId: command.UserId.Value, ActorUserId: caller.Value);
        return await IsEligibleAsync(found.Value, parties.AssigneeUserId, cancellationToken)
            ? await MoveAsync(found.Value, parties, cancellationToken)
            : Refusal.Invalid(AssignmentsErrorCodes.AssigneeNotEligible);
    }

    private async Task<bool> IsEligibleAsync(QueueRecord found, long assigneeUserId, CancellationToken cancellationToken)
    {
        var decision = await eligibility.ScopeForUserAsync(assigneeUserId, found.Queue.Registration.QueuePermissionKey, cancellationToken);
        return decision.Reaches(found.Record.OrganizationId);
    }

    private async Task<Outcome<Done>> MoveAsync(QueueRecord found, AssignmentParties parties, CancellationToken cancellationToken)
    {
        var open = await rows.FindOpenAsync(found.Record.Id, cancellationToken);
        if (open?.AssigneeUserId == parties.AssigneeUserId)
        {
            return Done.Value;
        }

        var saved = open is null
            ? await rows.OpenAsync(found.Ref, parties, cancellationToken)
            : await rows.ReassignAsync(open, parties, cancellationToken);
        return saved ? Done.Value : Refusal.Conflict(AssignmentsErrorCodes.AlreadyClaimed);
    }
}
