using Starter.Domain.Workflow;

namespace Starter.Application.Workflow;

/// <summary>
/// The engine's answer to "may this caller take this action on this record now": <see cref="Allowed"/> with the
/// rule to apply, or <see cref="Refused"/> with the reason. The engine never changes a record; the caller applies an
/// allowed rule and records the history.
/// </summary>
public abstract record TransitionDecision
{
    private TransitionDecision()
    {
    }

    /// <summary>The transition may proceed.</summary>
    /// <param name="Rule">The rule to apply (its <see cref="TransitionRule.ToStatus"/> is the new status).</param>
    /// <param name="Reason">The trimmed reason to record, or null (always null when the rule takes no reason).</param>
    public sealed record Allowed(TransitionRule Rule, string? Reason) : TransitionDecision;

    /// <summary>The transition is refused.</summary>
    /// <param name="Refusal">Why.</param>
    /// <param name="ReasonFieldKey">The reason field the refusal is about, for the two reason refusals; otherwise null.</param>
    public sealed record Refused(WorkflowRefusal Refusal, string? ReasonFieldKey) : TransitionDecision;
}
