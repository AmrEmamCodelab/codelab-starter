using Starter.Application.Messaging;

namespace Starter.Infrastructure.Messaging;

/// <summary>
/// The timing of the drain: how long a claim hides a message, how long a failed attempt waits, and how much one pass
/// takes on. One owner, so the lease and the back-off cannot drift apart.
/// </summary>
public static class OutboxDrainSchedule
{
    /// <summary>Lease length in minutes (see <see cref="Lease"/>).</summary>
    public const int LeaseMinutes = 5;

    /// <summary>Back-off after the first failed attempt, in minutes.</summary>
    public const int FirstBackoffMinutes = 1;

    /// <summary>Back-off after the second failed attempt, in minutes.</summary>
    public const int SecondBackoffMinutes = 5;

    /// <summary>Back-off after the third failed attempt, in minutes.</summary>
    public const int ThirdBackoffMinutes = 15;

    /// <summary>Back-off after the fourth and every later failed attempt, in minutes.</summary>
    public const int LongBackoffMinutes = 60;

    /// <summary>
    /// The most claims one pass makes, so a pass has a bounded duration and a large backlog is worked off over several
    /// passes instead of one pass holding thousands of leases.
    /// </summary>
    public const int MaxBatchesPerPass = 20;

    private const int SecondAttempt = 2;
    private const int ThirdAttempt = 3;

    /// <summary>
    /// How long a claim hides a message from other drains: longer than any delivery, so a live drain keeps its claim,
    /// and short enough that a drain that died mid-delivery releases its messages within one drain interval.
    /// </summary>
    public static TimeSpan Lease { get; } = TimeSpan.FromMinutes(LeaseMinutes);

    /// <summary>
    /// The wait after failed attempt <paramref name="attempt"/>: 1, 5, 15, then 60 minutes. Short at first so a transport
    /// blip clears quickly; long at the end so <see cref="OutboxLimits.MaxAttempts"/> attempts span hours, the window
    /// an operator needs to fix a broken relay.
    /// </summary>
    public static TimeSpan BackoffAfter(int attempt) => attempt switch
    {
        < SecondAttempt => TimeSpan.FromMinutes(FirstBackoffMinutes),
        SecondAttempt => TimeSpan.FromMinutes(SecondBackoffMinutes),
        ThirdAttempt => TimeSpan.FromMinutes(ThirdBackoffMinutes),
        _ => TimeSpan.FromMinutes(LongBackoffMinutes),
    };
}

/// <summary>The codes the drain stores in <see cref="OutboxEntry.LastError"/> for failures it decides itself.</summary>
public static class OutboxErrorCodes
{
    /// <summary>No <see cref="IOutboxHandler"/> is registered for the message type.</summary>
    public const string NoHandler = "OUTBOX_NO_HANDLER";

    /// <summary>The lease of the final attempt expired without an outcome (the drain died mid-delivery).</summary>
    public const string LeaseExpired = "OUTBOX_LEASE_EXPIRED";
}
