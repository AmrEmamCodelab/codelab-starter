using Starter.Application.Messaging;

namespace Starter.Modules.Notifications.Application.Delivery;

/// <summary>Delivers <see cref="OutboxMessageTypes.NotificationEmail"/> messages (one per notification e-mail).</summary>
public sealed class NotificationEmailHandler(NotificationDeliveries deliveries, EmailChannel channel) : IOutboxHandler
{
    /// <inheritdoc />
    public string MessageType => OutboxMessageTypes.NotificationEmail;

    /// <inheritdoc />
    public Task HandleAsync(OutboxDelivery delivery, CancellationToken cancellationToken) =>
        deliveries.DeliverAsync(delivery, channel, cancellationToken);
}

/// <summary>Delivers <see cref="OutboxMessageTypes.NotificationSms"/> messages (one per notification SMS).</summary>
public sealed class NotificationSmsHandler(NotificationDeliveries deliveries, SmsChannel channel) : IOutboxHandler
{
    /// <inheritdoc />
    public string MessageType => OutboxMessageTypes.NotificationSms;

    /// <inheritdoc />
    public Task HandleAsync(OutboxDelivery delivery, CancellationToken cancellationToken) =>
        deliveries.DeliverAsync(delivery, channel, cancellationToken);
}
