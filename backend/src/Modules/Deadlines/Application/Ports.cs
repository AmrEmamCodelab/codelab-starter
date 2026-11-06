using Starter.Modules.Deadlines.Domain;

namespace Starter.Modules.Deadlines.Application;

/// <summary>The record deadlines (<c>deadlines.RecordDeadlines</c>) and their unit of work.</summary>
public interface IRecordDeadlineStore
{
    /// <summary>The open deadline of the record (tracked), or null.</summary>
    Task<RecordDeadline?> FindOpenAsync(long recordId, string recordType, CancellationToken cancellationToken);

    /// <summary>The open deadlines of <paramref name="recordIds"/> (no tracking; records without one are absent).</summary>
    Task<IReadOnlyList<RecordDeadline>> OpenForAsync(IReadOnlyCollection<long> recordIds, CancellationToken cancellationToken);

    /// <summary>At most <paramref name="take"/> open, unmarked deadlines due at or before <paramref name="now"/>, earliest first (tracked).</summary>
    Task<IReadOnlyList<RecordDeadline>> DueForBreachAsync(DateTimeOffset now, int take, CancellationToken cancellationToken);

    /// <summary>At most <paramref name="take"/> open deadlines whose breach is marked and not yet escalated, earliest first (tracked).</summary>
    Task<IReadOnlyList<RecordDeadline>> AwaitingEscalationAsync(int take, CancellationToken cancellationToken);

    /// <summary>Tracks a new deadline; it is written by the next <see cref="SaveChangesAsync"/>.</summary>
    void Add(RecordDeadline deadline);

    /// <summary>Writes the tracked changes.</summary>
    Task SaveChangesAsync(CancellationToken cancellationToken);
}

/// <summary>
/// The staff who hold a permission, for the escalation of a breach on an unassigned record (the holders of the record
/// type's queue permission). Optional: when no implementation is registered, an unassigned breach is escalated to no
/// one and logged. Candidate kernel port (<c>Starter.Application.Authorization</c>, implemented by the Access module):
/// see MODULE.md, Gotchas.
/// </summary>
public interface IPermissionHolderDirectory
{
    /// <summary>
    /// The ids of the active users who hold <paramref name="permissionKey"/> at a scope that reaches a record of
    /// <paramref name="organizationId"/> (Global, or OwnOrganization of that organization; null: a record without an
    /// organization, reached by Global holders only).
    /// </summary>
    Task<IReadOnlyList<long>> HoldersAsync(string permissionKey, long? organizationId, CancellationToken cancellationToken);
}
