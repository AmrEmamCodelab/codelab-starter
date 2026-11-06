namespace Starter.Application.Records;

/// <summary>Where a record stands against the deadline of its current status (stored nowhere; sent as its int value).</summary>
public enum SlaState
{
    /// <summary>The deadline is further away than the due-soon window.</summary>
    OnTrack = 1,

    /// <summary>The deadline is within the due-soon window and not yet passed.</summary>
    DueSoon = 2,

    /// <summary>The deadline has passed.</summary>
    Breached = 3,
}

/// <summary>The SLA badge of one record: its state and the deadline of its current status.</summary>
/// <param name="State">On track, due soon or breached.</param>
/// <param name="DueAt">The deadline.</param>
public sealed record SlaBadge(SlaState State, DateTimeOffset DueAt);

/// <summary>
/// The SLA badges of records, implemented by the Deadlines module and shown in the queue and detail views. Optional:
/// when no implementation is registered, consumers resolve <c>IEnumerable&lt;IRecordSlaReader&gt;</c> and show no badge.
/// </summary>
public interface IRecordSlaReader
{
    /// <summary>
    /// The badge of each of <paramref name="recordIds"/> whose current status has a running SLA (records without one,
    /// or in a terminal status, are absent). Computed at the current <c>TimeProvider</c> time.
    /// </summary>
    Task<IReadOnlyDictionary<long, SlaBadge>> BadgesAsync(IReadOnlyCollection<long> recordIds, CancellationToken cancellationToken);
}
