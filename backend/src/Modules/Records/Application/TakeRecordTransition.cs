using Starter.Application.Records;
using Starter.Application.Results;
using Starter.Application.Validation;
using Starter.Domain.Auditing;

namespace Starter.Modules.Records.Application;

/// <summary>The body of <c>POST api/records/{recordType}/{id}/transitions</c>.</summary>
/// <param name="Action">The action key (one of the record's allowed actions).</param>
/// <param name="Reason">The reason, or null; the workflow decides whether it is required and how long it may be.</param>
public sealed record TransitionCommand(string? Action, string? Reason);

/// <summary>
/// The shape of a transition request: an action key is present and fits the timeline column. The reason is the
/// workflow's to judge (400 <c>WORKFLOW_REASON_REQUIRED</c> / <c>WORKFLOW_REASON_TOO_LONG</c> on its field).
/// </summary>
public sealed class TransitionCommandValidator : IValidator<TransitionCommand>
{
    /// <inheritdoc />
    public ValidationErrors Validate(TransitionCommand input)
    {
        ArgumentNullException.ThrowIfNull(input);
        return new ValidationErrors().RequireText(FieldNames.Of(nameof(TransitionCommand.Action)), input.Action, RecordHistoryLimits.ActionMaxLength);
    }
}

/// <summary>
/// <c>POST api/records/{recordType}/{id}/transitions</c>: takes one action through <see cref="RecordTransitions"/> (the
/// submit actions with their checks) and answers the record's new detail, so the screen re-renders from the server's
/// allowed actions.
/// </summary>
public sealed class TakeRecordTransition(
    IValidator<TransitionCommand> validator, RecordTransitions transitions, RecordLoader loader, GetRecordDetail details)
{
    /// <summary>The new detail, or the refusal (400, 403, 404, 409).</summary>
    public async Task<Outcome<RecordDetailView>> ExecuteAsync(
        string? recordType, long recordId, TransitionCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        var errors = validator.Validate(command);
        if (!errors.IsValid)
        {
            return Refusal.Invalid(errors);
        }

        var result = await transitions.TakeAsync(recordType, recordId, command.Action!, command.Reason, cancellationToken);
        if (result is TransitionResult.Refused refused)
        {
            return refused.Refusal;
        }

        var reload = await loader.LoadAsync(recordType, recordId, cancellationToken);
        return reload.IsSuccess ? await details.DetailOfAsync(reload.Value, cancellationToken) : reload.Refusal;
    }
}
