namespace Starter.Application.Messaging;

/// <summary>
/// The outbox: messages a module wants delivered later (an activation e-mail, a notification). A message is written
/// as <see cref="OutboxMessageStatus.Pending"/>; the drain (<see cref="IOutboxDrainRunner"/>) later hands it to the one
/// <see cref="IOutboxHandler"/> registered for its <see cref="OutboxMessage.Type"/>.
/// </summary>
/// <remarks>
/// Not yet transactional with the caller: <see cref="EnqueueAsync"/> saves in its own database context, so a caller
/// that enqueues and then fails its own save leaves the message behind, and the reverse. Enqueue after the caller's
/// save succeeded, and keep payloads safe to deliver late or twice.
/// </remarks>
public interface IOutbox
{
    /// <summary>Writes <paramref name="message"/> as a Pending row immediately.</summary>
    /// <exception cref="ArgumentException">The type is blank or longer than <see cref="OutboxLimits.TypeMaxLength"/>, or the payload is blank.</exception>
    Task EnqueueAsync(OutboxMessage message, CancellationToken cancellationToken);
}

/// <summary>One outbox message.</summary>
/// <param name="Type">What the message is, an <see cref="OutboxMessageTypes"/> constant (the drain routes on it).</param>
/// <param name="PayloadJson">
/// The message data as JSON. Never a password, token, sealed ticket or raw personal data beyond what delivery needs.
/// </param>
/// <param name="NotBefore">The earliest delivery time, or null for as soon as possible.</param>
public sealed record OutboxMessage(string Type, string PayloadJson, DateTimeOffset? NotBefore);

/// <summary>The outbox message types (<see cref="OutboxMessage.Type"/>).</summary>
public static class OutboxMessageTypes
{
    /// <summary>An account activation link to deliver; the payload is an <see cref="AccountActivationPayload"/>.</summary>
    public const string AccountActivation = "account-activation";

    /// <summary>A notification e-mail to send (payload owned by the Notifications module).</summary>
    public const string NotificationEmail = "notification-email";

    /// <summary>A notification SMS to send (payload owned by the Notifications module).</summary>
    public const string NotificationSms = "notification-sms";
}

/// <summary>
/// The payload of an <see cref="OutboxMessageTypes.AccountActivation"/> message. It names the ticket, never carries
/// it: the sealed ticket string stays out of the database.
/// </summary>
/// <param name="UserPublicId">The user's PublicId (label <c>users</c>).</param>
/// <param name="TicketId">The activation ticket's public id.</param>
/// <param name="ExpiresAt">When the ticket stops working.</param>
public sealed record AccountActivationPayload(string UserPublicId, string TicketId, DateTimeOffset ExpiresAt);

/// <summary>The delivery state of an outbox row. Stored as the integer value.</summary>
public enum OutboxMessageStatus
{
    /// <summary>Written, not delivered yet.</summary>
    Pending = 1,

    /// <summary>Delivered.</summary>
    Sent = 2,

    /// <summary>
    /// Delivery gave up: no handler for the type, or <see cref="OutboxLimits.MaxAttempts"/> attempts failed. A Failed
    /// row is never delivered again; an operator decides whether the recipient still needs it.
    /// </summary>
    Failed = 3,

    /// <summary>Claimed by a drain that holds a lease until <c>LockedUntil</c>; reclaimed only after the lease expires.</summary>
    Processing = 4,
}

/// <summary>The outbox limits.</summary>
public static class OutboxLimits
{
    /// <summary>The longest <see cref="OutboxMessage.Type"/>.</summary>
    public const int TypeMaxLength = 128;

    /// <summary>
    /// Delivery attempts before a message becomes <see cref="OutboxMessageStatus.Failed"/>. An attempt is counted when a
    /// drain claims the message, so a message whose delivery crashes the process also runs out of attempts.
    /// </summary>
    public const int MaxAttempts = 8;
}
