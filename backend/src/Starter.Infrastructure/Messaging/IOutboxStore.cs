using Starter.Application.Messaging;

namespace Starter.Infrastructure.Messaging;

/// <summary>
/// The read side of the outbox the drain works through: claim due messages under a lease, then record one outcome per
/// claim. Every state change is guarded so a drain whose lease expired cannot overwrite the outcome of the drain that
/// reclaimed the message.
/// </summary>
public interface IOutboxStore
{
    /// <summary>
    /// Marks <see cref="OutboxMessageStatus.Processing"/> rows whose lease expired on their final attempt as
    /// <see cref="OutboxMessageStatus.Failed"/> (<see cref="OutboxErrorCodes.LeaseExpired"/>) and returns how many.
    /// </summary>
    Task<int> FailAbandonedAsync(DateTimeOffset now, CancellationToken cancellationToken);

    /// <summary>
    /// Claims up to <see cref="OutboxClaim.BatchSize"/> due messages in one atomic statement, oldest first: Pending rows
    /// whose <c>NotBefore</c> has passed, and Processing rows whose lease expired with attempts left. Each claimed row
    /// becomes Processing with the new lease and one more attempt.
    /// </summary>
    Task<IReadOnlyList<OutboxDelivery>> ClaimAsync(OutboxClaim claim, CancellationToken cancellationToken);

    /// <summary>Marks a claimed message Sent. A message already Sent, Failed or back to Pending is left as it is.</summary>
    Task MarkSentAsync(long messageId, DateTimeOffset sentAt, CancellationToken cancellationToken);

    /// <summary>Returns a claimed message to Pending until <see cref="OutboxRetry.NotBefore"/>, if this attempt still holds it.</summary>
    Task MarkRetryAsync(OutboxRetry retry, CancellationToken cancellationToken);

    /// <summary>Marks a claimed message Failed, if this attempt still holds it.</summary>
    Task MarkFailedAsync(OutboxAttemptFailure failure, CancellationToken cancellationToken);
}

/// <summary>One claim.</summary>
/// <param name="Now">The time the claim is made; rows due at or before it qualify.</param>
/// <param name="LockedUntil">The lease the claimed rows get.</param>
/// <param name="BatchSize">The most rows to claim.</param>
public sealed record OutboxClaim(DateTimeOffset Now, DateTimeOffset LockedUntil, int BatchSize);

/// <summary>A failed attempt that will be retried.</summary>
/// <param name="MessageId">The message.</param>
/// <param name="Attempt">The attempt that failed (the guard: a later claim has a higher number).</param>
/// <param name="NotBefore">When the message is due again.</param>
/// <param name="ErrorCode">The classified failure code.</param>
public sealed record OutboxRetry(long MessageId, int Attempt, DateTimeOffset NotBefore, string ErrorCode);

/// <summary>A failed attempt that ends delivery.</summary>
/// <param name="MessageId">The message.</param>
/// <param name="Attempt">The attempt that failed (the guard).</param>
/// <param name="ErrorCode">The classified failure code or an <see cref="OutboxErrorCodes"/> constant.</param>
public sealed record OutboxAttemptFailure(long MessageId, int Attempt, string ErrorCode);
