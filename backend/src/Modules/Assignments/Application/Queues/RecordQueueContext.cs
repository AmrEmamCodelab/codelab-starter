using System.Globalization;
using Starter.Application.Authorization;
using Starter.Application.Records;
using Starter.Domain.Workflow;

namespace Starter.Modules.Assignments.Application.Queues;

/// <summary>One record of a queue as a request names it: the record type route segment and the decoded record id.</summary>
/// <param name="RecordType">The record type key from the route (checked when the queue is opened; unknown answers 404).</param>
/// <param name="RecordId">The internal record id (decoded from its PublicId).</param>
public sealed record QueueItemKey(string RecordType, long RecordId);

/// <summary>
/// The queue of one record type as the current caller sees it: the record type's registration and workflow, and the
/// caller's organization scope for its queue permission. The one owner of "which records are in the queue".
/// </summary>
public sealed class RecordQueueContext
{
    internal RecordQueueContext(RecordTypeRegistration registration, TransitionCatalog catalog, ScopeDecision scope)
    {
        Registration = registration;
        Catalog = catalog;
        Scope = scope;
        OpenStatuses = [.. catalog.Statuses.Where(status => !status.IsInitial && !status.IsTerminal).Select(status => status.Status)];
    }

    /// <summary>The record type key.</summary>
    public string RecordType => Registration.RecordType;

    /// <summary>The record type's registration (queue permission key).</summary>
    public RecordTypeRegistration Registration { get; }

    /// <summary>The caller's decision for the queue permission (allowed: global or own organization).</summary>
    public ScopeDecision Scope { get; }

    /// <summary>
    /// The statuses a queue lists: every status that is neither the initial (draft) status nor terminal, in catalog
    /// order. A record leaves the queue when it closes.
    /// </summary>
    public IReadOnlyList<int> OpenStatuses { get; }

    private TransitionCatalog Catalog { get; }

    /// <summary>The wire key of <paramref name="status"/> (<c>under-review</c>), or its number when the catalog lacks it.</summary>
    public string StatusKey(int status) =>
        Catalog.FindStatus(status)?.Key ?? status.ToString(CultureInfo.InvariantCulture);

    /// <summary>
    /// The status filter of a list request: every open status for no filter, the one status when it is open, null when
    /// it is not a queue status (a draft, a closed or an unknown status: the request is invalid).
    /// </summary>
    public IReadOnlyList<int>? StatusFilter(int? status) => status switch
    {
        null => OpenStatuses,
        { } one when OpenStatuses.Contains(one) => [one],
        _ => null,
    };

    /// <summary>True when <paramref name="record"/> is of this record type and inside the caller's organization scope.</summary>
    public bool Reaches(RecordSummary record)
    {
        ArgumentNullException.ThrowIfNull(record);
        return string.Equals(record.RecordType, RecordType, StringComparison.Ordinal) && Scope.Reaches(record.OrganizationId);
    }

    /// <summary>True when <paramref name="record"/> is in the queue: submitted, not closed, in an open status.</summary>
    public bool IsOpen(RecordSummary record)
    {
        ArgumentNullException.ThrowIfNull(record);
        return !record.IsTerminal && record.SubmittedAt is not null && OpenStatuses.Contains(record.Status);
    }
}
