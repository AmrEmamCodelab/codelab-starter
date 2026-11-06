using Starter.Application.Workflow;

namespace Starter.Workflow.Testing;

/// <summary>The engine's decision on one <see cref="TransitionFixture.Do"/>, with the expectations to assert on it.</summary>
public sealed class TransitionOutcome
{
    internal TransitionOutcome(TransitionFixture fixture, string actionKey, TransitionDecision decision)
    {
        Fixture = fixture;
        ActionKey = actionKey;
        Decision = decision;
    }

    /// <summary>The fixture the action was taken from.</summary>
    public TransitionFixture Fixture { get; }

    /// <summary>The action taken.</summary>
    public string ActionKey { get; }

    /// <summary>The engine's decision.</summary>
    public TransitionDecision Decision { get; }

    /// <summary>Asserts the action was allowed and lands on <paramref name="status"/>; continues from there.</summary>
    public TransitionFixture ExpectStatus(int status)
    {
        var allowed = RequireAllowed();
        if (allowed.Rule.ToStatus != status)
        {
            throw new WorkflowExpectationException(
                $"'{ActionKey}' from status {Fixture.Status} expected to land on {status} but lands on {allowed.Rule.ToStatus}.");
        }

        return Fixture.From(status);
    }

    /// <summary>Asserts the action was allowed and lands on a terminal status.</summary>
    public TransitionOutcome ExpectTerminal()
    {
        var allowed = RequireAllowed();
        if (!Fixture.Catalog.IsTerminal(allowed.Rule.ToStatus))
        {
            throw new WorkflowExpectationException(
                $"'{ActionKey}' from status {Fixture.Status} lands on {allowed.Rule.ToStatus}, which is not terminal.");
        }

        return this;
    }

    /// <summary>Asserts the action was refused with <paramref name="refusal"/>; continues from the unchanged status.</summary>
    public TransitionFixture ExpectRefused(WorkflowRefusal refusal)
    {
        if (Decision is not TransitionDecision.Refused refused)
        {
            throw new WorkflowExpectationException($"'{ActionKey}' from status {Fixture.Status} expected refusal {refusal} but was allowed.");
        }

        if (refused.Refusal != refusal)
        {
            throw new WorkflowExpectationException($"'{ActionKey}' from status {Fixture.Status} expected refusal {refusal} but got {refused.Refusal}.");
        }

        return Fixture;
    }

    private TransitionDecision.Allowed RequireAllowed() => Decision as TransitionDecision.Allowed
        ?? throw new WorkflowExpectationException(
            $"'{ActionKey}' from status {Fixture.Status} expected to be allowed but was refused ({((TransitionDecision.Refused)Decision).Refusal}).");
}
