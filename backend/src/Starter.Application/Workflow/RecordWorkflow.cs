using Starter.Domain.Workflow;

namespace Starter.Application.Workflow;

/// <summary>The <see cref="IRecordWorkflow{TRecord}"/> facade: the record's catalog and facts, handed to <see cref="RecordWorkflowEngine"/>.</summary>
/// <typeparam name="TRecord">The record entity.</typeparam>
public sealed class RecordWorkflow<TRecord>(IRecordWorkflowCatalogs catalogs, IWorkflowRecordReader<TRecord> reader) : IRecordWorkflow<TRecord>
{
    /// <inheritdoc />
    public IReadOnlyList<AllowedAction> AllowedActions(TRecord record, WorkflowActor actor)
    {
        ArgumentNullException.ThrowIfNull(actor);
        return RecordWorkflowEngine.AllowedActions(
            CatalogOf(record),
            reader.StatusOf(record),
            actor.PermissionKeys.Contains,
            isOwner: IsOwner(record, actor),
            actor.Side);
    }

    /// <inheritdoc />
    public TransitionDecision Decide(TRecord record, string actionKey, string? reason, WorkflowActor actor)
    {
        ArgumentNullException.ThrowIfNull(actor);
        return RecordWorkflowEngine.Decide(
            CatalogOf(record),
            reader.StatusOf(record),
            actionKey,
            reason,
            new WorkflowStanding(actor.PermissionKeys.Contains, IsOwner: IsOwner(record, actor), actor.Side));
    }

    private TransitionCatalog CatalogOf(TRecord record)
    {
        var recordType = reader.RecordTypeOf(record);
        return catalogs.Find(recordType)
            ?? throw new InvalidOperationException($"No workflow is registered for record type '{recordType}'; call AddRecordWorkflow<TDefinition>() in its module.");
    }

    private bool IsOwner(TRecord record, WorkflowActor actor) => reader.OwnerUserIdOf(record) == actor.UserId;
}
