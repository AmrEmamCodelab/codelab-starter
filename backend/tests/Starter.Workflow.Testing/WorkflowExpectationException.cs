namespace Starter.Workflow.Testing;

/// <summary>A <see cref="TransitionFixture"/> expectation failed; the message says what was expected and what happened.</summary>
public sealed class WorkflowExpectationException : Exception
{
    /// <summary>Creates the exception.</summary>
    public WorkflowExpectationException(string message)
        : base(message)
    {
    }
}
