namespace Starter.Application.Workflow;

/// <summary>
/// The workflow of any record type, over one record entity: what the caller may do now, and whether a requested
/// action may proceed. It never changes the record; the caller applies an allowed rule, saves and writes the history.
/// </summary>
/// <typeparam name="TRecord">The record entity, read through <see cref="IWorkflowRecordReader{TRecord}"/>.</typeparam>
public interface IRecordWorkflow<in TRecord>
{
    /// <summary>The actions of the actor's side the actor may take now; empty for a terminal record.</summary>
    /// <exception cref="InvalidOperationException">No workflow is registered for the record's type.</exception>
    IReadOnlyList<AllowedAction> AllowedActions(TRecord record, WorkflowActor actor);

    /// <summary>Decides <paramref name="actionKey"/> with <paramref name="reason"/> for the actor.</summary>
    /// <exception cref="InvalidOperationException">No workflow is registered for the record's type.</exception>
    TransitionDecision Decide(TRecord record, string actionKey, string? reason, WorkflowActor actor);
}
