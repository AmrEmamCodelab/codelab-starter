namespace Starter.Application.Workflow;

/// <summary>
/// Reads the three facts the workflow needs from a record of type <typeparamref name="TRecord"/>. Implemented by the
/// module that owns the record entity, so the workflow kernel never references a record type.
/// </summary>
/// <typeparam name="TRecord">The record entity.</typeparam>
public interface IWorkflowRecordReader<in TRecord>
{
    /// <summary>The record type key, which selects the catalog.</summary>
    string RecordTypeOf(TRecord record);

    /// <summary>The record's current status.</summary>
    int StatusOf(TRecord record);

    /// <summary>The internal user id of the record's owner.</summary>
    long OwnerUserIdOf(TRecord record);
}
