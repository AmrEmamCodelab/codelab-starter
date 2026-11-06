using Microsoft.Extensions.Logging;
using Starter.Application.Messaging;
using Starter.Application.Notifications;
using Starter.Domain.Exceptions;
using Starter.Modules.Notifications.Application.Rendering;
using Starter.Modules.Notifications.Domain;

namespace Starter.Modules.Notifications.Application.Delivery;

/// <summary>
/// Delivers one outbox message of a notification channel. Idempotent per notification and channel (so per outbox
/// message id): a channel that is no longer Pending is not sent again. The recipient's address and language are read
/// now (<see cref="INotificationRecipients"/>); a user without an address for the channel ends NoAddress, not retried.
/// A sender failure propagates, so the outbox retries with back-off. Logs carry ids only, never addresses or texts.
/// </summary>
public sealed partial class NotificationDeliveries(
    INotificationStore store,
    INotificationRecipients recipients,
    NotificationComposer composer,
    TimeProvider timeProvider,
    ILogger<NotificationDeliveries> logger)
{
    /// <summary>Delivers <paramref name="delivery"/> on <paramref name="channel"/>.</summary>
    public async Task DeliverAsync(OutboxDelivery delivery, IDeliveryChannel channel, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(delivery);
        ArgumentNullException.ThrowIfNull(channel);
        var payload = DeliveryPayload.TryRead(delivery.PayloadJson);
        if (payload is null || payload.Channel != channel.Channel)
        {
            LogUnreadablePayload(logger, delivery.MessageId, delivery.Type);
            return;
        }

        var notification = await store.FindAsync(payload.NotificationId, cancellationToken);
        if (notification is null)
        {
            LogMissingNotification(logger, delivery.MessageId, payload.NotificationId);
            return;
        }

        if (notification.StateOf(channel.Channel) != ChannelDeliveryState.Pending)
        {
            return;
        }

        await DeliverPendingAsync(notification, channel, cancellationToken);
    }

    private async Task DeliverPendingAsync(Notification notification, IDeliveryChannel channel, CancellationToken cancellationToken)
    {
        var target = await TargetOfAsync(notification, channel, cancellationToken);
        if (target is null)
        {
            LogNoAddress(logger, notification.Id, (int)channel.Channel);
            await CompleteAsync(notification, channel, ChannelDeliveryState.NoAddress, cancellationToken);
            return;
        }

        var placeholders = PlaceholderJson.Read(notification.PlaceholdersJson);
        var slot = new TemplateSlot(channel.Channel, target.Language);
        var rendered = await composer.ComposeOneAsync(notification.TemplateKey, placeholders, slot, cancellationToken);
        if (!rendered.IsSuccess)
        {
            throw new BusinessRuleException(rendered.Refusal.Code, $"Notification {notification.Id} could not be rendered for channel {(int)channel.Channel}.");
        }

        await channel.SendAsync(target, rendered.Value, cancellationToken);
        await CompleteAsync(notification, channel, ChannelDeliveryState.Sent, CancellationToken.None);
    }

    private async Task<DeliveryTarget?> TargetOfAsync(Notification notification, IDeliveryChannel channel, CancellationToken cancellationToken)
    {
        if (notification.UserId is not { } userId)
        {
            return channel.Channel == NotificationChannels.Email && notification.EmailAddress is { } address && notification.Language is { } language
                ? new DeliveryTarget(address, language)
                : null;
        }

        var recipient = await recipients.FindAsync(userId, cancellationToken);
        return recipient is not null && channel.FindAddress(recipient) is { } reach ? new DeliveryTarget(reach, recipient.Language) : null;
    }

    private async Task CompleteAsync(Notification notification, IDeliveryChannel channel, ChannelDeliveryState outcome, CancellationToken cancellationToken)
    {
        notification.Complete(channel.Channel, outcome, timeProvider.GetUtcNow());
        await store.SaveChangesAsync(cancellationToken);
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Outbox message {MessageId} of type {MessageType} has no readable notification payload; nothing delivered.")]
    private static partial void LogUnreadablePayload(ILogger logger, long messageId, string messageType);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Outbox message {MessageId} names notification {NotificationId}, which does not exist; nothing delivered.")]
    private static partial void LogMissingNotification(ILogger logger, long messageId, long notificationId);

    [LoggerMessage(Level = LogLevel.Information, Message = "Notification {NotificationId} has no address on channel {Channel}; delivery skipped.")]
    private static partial void LogNoAddress(ILogger logger, long notificationId, int channel);
}
