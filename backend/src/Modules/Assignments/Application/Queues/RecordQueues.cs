using Starter.Application.Authorization;
using Starter.Application.Records;
using Starter.Application.Results;
using Starter.Application.Workflow;
using Starter.Domain.Records;

namespace Starter.Modules.Assignments.Application.Queues;

/// <summary>
/// Opens a record type's queue for the current caller and finds its records. A queue exists for every record type a
/// module registers (<see cref="IRecordTypeRegistrationSource"/>) WITH a workflow (<see cref="IRecordWorkflowCatalogs"/>);
/// the caller needs the registration's queue permission, and their organization scope for it narrows every read.
/// Record facts come only from the kernel <see cref="IRecordDirectory"/> (Records module).
/// </summary>
public sealed class RecordQueues(
    IEnumerable<IRecordTypeRegistrationSource> registrations,
    IRecordWorkflowCatalogs catalogs,
    IOrganizationScope organizationScope,
    IRecordDirectory directory)
{
    /// <summary>
    /// The queue of <paramref name="recordType"/>: 404 <see cref="AssignmentsErrorCodes.RecordTypeUnknown"/> for a
    /// malformed or unregistered type (or one without a workflow), 403 <see cref="AssignmentsErrorCodes.QueueForbidden"/>
    /// when the caller lacks its queue permission.
    /// </summary>
    public async Task<Outcome<RecordQueueContext>> OpenAsync(string recordType, CancellationToken cancellationToken)
    {
        if (!RecordTypeKeyFormat.IsValid(recordType))
        {
            return Refusal.NotFound(AssignmentsErrorCodes.RecordTypeUnknown);
        }

        var registration = registrations
            .SelectMany(source => source.Registrations)
            .FirstOrDefault(candidate => string.Equals(candidate.RecordType, recordType, StringComparison.Ordinal));
        if (registration is null || catalogs.Find(recordType) is not { } catalog)
        {
            return Refusal.NotFound(AssignmentsErrorCodes.RecordTypeUnknown);
        }

        var scope = await organizationScope.ScopeForAsync(registration.QueuePermissionKey, cancellationToken);
        return scope.Allowed
            ? new RecordQueueContext(registration, catalog, scope)
            : Refusal.Forbidden(AssignmentsErrorCodes.QueueForbidden);
    }

    /// <summary>
    /// The record of <paramref name="item"/> when the caller reaches it in its queue: 404 <see cref="RecordsErrorCodes.NotFound"/>
    /// for a missing record, one of another record type or one outside the caller's organization scope (never confirmed
    /// to exist).
    /// </summary>
    public async Task<Outcome<QueueRecord>> FindAsync(QueueItemKey item, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(item);
        var opened = await OpenAsync(item.RecordType, cancellationToken);
        if (!opened.IsSuccess)
        {
            return opened.Refusal;
        }

        var queue = opened.Value;
        var found = await directory.GetAsync([item.RecordId], cancellationToken);
        var record = found.FirstOrDefault(summary => summary.Id == item.RecordId);
        return record is not null && queue.Reaches(record)
            ? new QueueRecord(queue, record)
            : Refusal.NotFound(RecordsErrorCodes.NotFound);
    }

    /// <summary>
    /// <see cref="FindAsync"/>, then 409 <see cref="AssignmentsErrorCodes.NotAssignable"/> when the record is a draft or
    /// closed (not in the queue, so nobody can take it).
    /// </summary>
    public async Task<Outcome<QueueRecord>> FindOpenAsync(QueueItemKey item, CancellationToken cancellationToken)
    {
        var found = await FindAsync(item, cancellationToken);
        if (!found.IsSuccess)
        {
            return found;
        }

        return found.Value.Queue.IsOpen(found.Value.Record) ? found : Refusal.Conflict(AssignmentsErrorCodes.NotAssignable);
    }
}

/// <summary>A record the caller reaches in a queue.</summary>
/// <param name="Queue">The queue (with the caller's scope).</param>
/// <param name="Record">The record's facts from the directory.</param>
public sealed record QueueRecord(RecordQueueContext Queue, RecordSummary Record)
{
    /// <summary>The record's kernel identity.</summary>
    public RecordRef Ref => new(Record.Id, Record.RecordType);
}
