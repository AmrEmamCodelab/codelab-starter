using Starter.Application.Results;
using Starter.Application.Validation;

namespace Starter.Application.Workflow;

/// <summary>
/// The ONE mapping of a workflow refusal to the wire (<see cref="Refusal"/>, then problem+json through
/// OutcomeResults). Endpoints never switch on <see cref="WorkflowRefusal"/> themselves.
/// </summary>
public static class WorkflowRefusals
{
    /// <summary>
    /// The field a reason error is reported on when the rule names no reason field: the <c>reason</c> member of the
    /// generic transition request body.
    /// </summary>
    public const string DefaultReasonField = "reason";

    /// <summary>The refusal for <paramref name="refused"/>: 404, 409, 403, or 400 with the reason field's error.</summary>
    /// <exception cref="ArgumentOutOfRangeException">The refusal is not a defined member (a programming fault).</exception>
    public static Refusal ToRefusal(TransitionDecision.Refused refused)
    {
        ArgumentNullException.ThrowIfNull(refused);
        return refused.Refusal switch
        {
            WorkflowRefusal.ActionUnknown => Refusal.NotFound(WorkflowErrorCodes.ActionUnknown),
            WorkflowRefusal.NotAllowedFromStatus => Refusal.Conflict(WorkflowErrorCodes.ActionNotAllowed),
            WorkflowRefusal.Forbidden => Refusal.Forbidden(WorkflowErrorCodes.Forbidden),
            WorkflowRefusal.ReasonRequired => ReasonRefusal(refused, ValidationMessages.Required, WorkflowErrorCodes.ReasonRequired),
            WorkflowRefusal.ReasonTooLong => ReasonRefusal(refused, ValidationMessages.TooLong, WorkflowErrorCodes.ReasonTooLong),
            _ => throw new ArgumentOutOfRangeException(nameof(refused), refused.Refusal, "Not a workflow refusal."),
        };
    }

    private static Refusal ReasonRefusal(TransitionDecision.Refused refused, string messageCode, string errorCode)
    {
        var field = string.IsNullOrWhiteSpace(refused.ReasonFieldKey) ? DefaultReasonField : refused.ReasonFieldKey;
        var errors = new Dictionary<string, string[]>(StringComparer.Ordinal) { [field] = [messageCode] };
        return Refusal.Invalid(errors, errorCode);
    }
}
