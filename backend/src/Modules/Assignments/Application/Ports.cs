using Starter.Application.Authorization;
using Starter.Application.Persistence;
using Starter.Modules.Assignments.Domain;

namespace Starter.Modules.Assignments.Application;

/// <summary>Which open assignment rows to list (<see cref="IAssignmentStore.OpenRecordIdsAsync"/>).</summary>
/// <param name="RecordType">Only rows of this record type.</param>
/// <param name="AssigneeUserId">Only rows this user holds, or null for every assignee.</param>
public sealed record OpenAssignmentFilter(string RecordType, long? AssigneeUserId);

/// <summary>The assignment rows (<c>assignments.RecordAssignments</c>), implemented in Infrastructure.</summary>
public interface IAssignmentStore : IUnitOfWork
{
    /// <summary>The open row of <paramref name="recordId"/> (tracked, so a release is saved), or null when nobody holds it.</summary>
    Task<RecordAssignment?> FindOpenAsync(long recordId, CancellationToken cancellationToken);

    /// <summary>The open rows of every record in <paramref name="recordIds"/> that has one (read only).</summary>
    Task<IReadOnlyList<RecordAssignment>> ListOpenAsync(IReadOnlyCollection<long> recordIds, CancellationToken cancellationToken);

    /// <summary>The record ids of the open rows that match <paramref name="filter"/>.</summary>
    Task<IReadOnlyList<long>> OpenRecordIdsAsync(OpenAssignmentFilter filter, CancellationToken cancellationToken);

    /// <summary>Stages a new row; written by the next save.</summary>
    void Add(RecordAssignment assignment);

    /// <summary>
    /// Writes the staged changes; false (nothing written, the staged changes dropped) when the database refused a
    /// second open row for a record (someone else claimed it first).
    /// </summary>
    Task<bool> TrySaveAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Writes the staged release of a record's current row, THEN adds <paramref name="replacement"/>, in one transaction (the
    /// one-open-row index needs that order). False, with nothing written, when another open row won the race.
    /// </summary>
    Task<bool> TryReassignAsync(RecordAssignment replacement, CancellationToken cancellationToken);
}

/// <summary>
/// How far ANOTHER user holds a permission (the assignee check of an assignment). The kernel answers this only for the
/// current user (<see cref="IOrganizationScope"/>), so the module owns this port until the kernel offers one; see
/// MODULE.md. The registered default refuses everyone, so an assignment fails closed.
/// </summary>
public interface IAssigneeEligibility
{
    /// <summary>
    /// The decision of user <paramref name="userId"/> for <paramref name="permissionKey"/>: denied for an unknown or
    /// inactive user and for a user without the key; otherwise the widest scope they hold it at.
    /// </summary>
    Task<ScopeDecision> ScopeForUserAsync(long userId, string permissionKey, CancellationToken cancellationToken);
}
