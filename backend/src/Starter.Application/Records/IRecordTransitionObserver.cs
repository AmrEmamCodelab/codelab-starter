namespace Starter.Application.Records;

/// <summary>
/// A committed status change of one record, as the Records module reports it to every
/// <see cref="IRecordTransitionObserver"/>. Statuses are the record type's own int values.
/// </summary>
/// <param name="RecordId">The internal record id.</param>
/// <param name="RecordType">The record type key.</param>
/// <param name="FromStatus">The status before the transition.</param>
/// <param name="ToStatus">The status after the transition.</param>
/// <param name="ActionKey">The workflow action that moved the record (for example <c>submit</c>).</param>
/// <param name="ActorUserId">The user who acted (the current user of the request).</param>
/// <param name="ToTerminal">True when <paramref name="ToStatus"/> is terminal.</param>
/// <param name="At">When the transition was committed (from <c>TimeProvider</c>).</param>
public sealed record RecordTransitioned(
    long RecordId,
    string RecordType,
    int FromStatus,
    int ToStatus,
    string ActionKey,
    long ActorUserId,
    bool ToTerminal,
    DateTimeOffset At);

/// <summary>
/// Reacts to a committed record transition (assignments close on a terminal status, deadlines start or stop a timer,
/// the owning module sends its notification). Optional: zero or more are registered.
/// </summary>
/// <remarks>
/// The Records module calls every observer after the transition is committed, in the same request, one after another.
/// The transition is already durable, so an observer that throws fails the request but does not undo the transition;
/// an observer keeps its own work idempotent per (<c>RecordId</c>, <c>ToStatus</c>, <c>At</c>).
/// </remarks>
public interface IRecordTransitionObserver
{
    /// <summary>Handles <paramref name="transition"/>.</summary>
    Task OnTransitionedAsync(RecordTransitioned transition, CancellationToken cancellationToken);
}
