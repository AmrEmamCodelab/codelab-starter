namespace Starter.Application.Messaging;

/// <summary>
/// Delivers one outbox message type. Exactly one handler is registered per <see cref="MessageType"/>
/// (<c>services.AddOutboxHandler&lt;THandler&gt;()</c>); the drain routes each claimed message to it.
/// </summary>
/// <remarks>
/// Delivery is at least once: a drain that crashes after the handler returned, or whose lease expired mid-delivery,
/// delivers the message again. A handler must therefore be idempotent per <see cref="OutboxDelivery.MessageId"/>
/// (the id is stable across attempts). Throwing schedules a retry with back-off until
/// <see cref="OutboxLimits.MaxAttempts"/>; the exception message is never stored, only its classified code.
/// </remarks>
public interface IOutboxHandler
{
    /// <summary>The <see cref="OutboxMessageTypes"/> constant this handler delivers.</summary>
    string MessageType { get; }

    /// <summary>Delivers <paramref name="delivery"/>; returns normally only when the message was delivered.</summary>
    Task HandleAsync(OutboxDelivery delivery, CancellationToken cancellationToken);
}

/// <summary>One claimed outbox message, as its handler sees it.</summary>
/// <param name="MessageId">The outbox row id: stable across attempts, the idempotency key of the delivery.</param>
/// <param name="Type">The message type (an <see cref="OutboxMessageTypes"/> constant).</param>
/// <param name="PayloadJson">The payload JSON as enqueued.</param>
/// <param name="Attempt">This attempt's number, starting at 1, at most <see cref="OutboxLimits.MaxAttempts"/>.</param>
public sealed record OutboxDelivery(long MessageId, string Type, string PayloadJson, int Attempt);

/// <summary>
/// One drain pass over the outbox: claims due messages, routes each to its handler and records the outcome. Scheduled
/// by the jobs module (<c>outbox-drain</c>); does nothing while <c>Messaging:Outbox:Enabled</c> is false.
/// </summary>
public interface IOutboxDrainRunner
{
    /// <summary>Delivers what is due now and returns the counts of this pass.</summary>
    Task<OutboxDrainResult> DrainOnceAsync(CancellationToken cancellationToken);
}

/// <summary>The counts of one drain pass.</summary>
/// <param name="Sent">Messages delivered and marked <see cref="OutboxMessageStatus.Sent"/>.</param>
/// <param name="Retried">Messages whose attempt failed and that wait for a later pass.</param>
/// <param name="Failed">Messages marked <see cref="OutboxMessageStatus.Failed"/> (no handler, out of attempts).</param>
public sealed record OutboxDrainResult(int Sent, int Retried, int Failed)
{
    /// <summary>A pass that did nothing (outbox disabled or nothing due).</summary>
    public static OutboxDrainResult None { get; } = new(Sent: 0, Retried: 0, Failed: 0);
}
