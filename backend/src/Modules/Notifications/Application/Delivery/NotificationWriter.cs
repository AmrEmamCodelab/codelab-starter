using Starter.Application.Messaging;
using Starter.Application.Notifications;
using Starter.Application.Results;
using Starter.Domain.Localization;
using Starter.Modules.Notifications.Application.Rendering;
using Starter.Modules.Notifications.Domain;

namespace Starter.Modules.Notifications.Application.Delivery;

/// <summary>
/// Writes notifications: renders every asked channel in both languages first (so a missing text or placeholder refuses
/// before anything is stored and the drain never meets one), saves the row with the bell texts, then queues one outbox
/// message per pending channel. The outbox saves on its own: a failure between the two saves leaves the channel
/// Pending without a message (logged by the caller's failure, never delivered twice).
/// </summary>
public sealed class NotificationWriter(
    NotificationComposer composer,
    INotificationStore store,
    IOutbox outbox,
    DeliverySettings settings,
    TimeProvider timeProvider)
{
    /// <summary>Writes a user's notification; the refusal when it is malformed or cannot be rendered.</summary>
    public async Task<Outcome<long>> WriteAsync(UserNotification notification, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(notification);
        if (!IsWellFormed(notification))
        {
            return Refusal.Unprocessable(NotificationsErrorCodes.InvalidNotification);
        }

        var channels = NotificationChannelSet.Split(notification.Channels)
            .Where(channel => channel != NotificationChannels.Sms || settings.SmsEnabled)
            .ToList();
        var rendered = await composer.ComposeAsync(notification.TemplateKey, notification.Placeholders, NotificationComposer.SlotsFor(channels), cancellationToken);
        if (!rendered.IsSuccess)
        {
            return rendered.Refusal;
        }

        var row = Notification.ForUser(new UserNotificationDraft(
            notification.UserId,
            notification.TemplateKey,
            PlaceholderJson.Write(notification.Placeholders),
            notification.Channels,
            notification.Link,
            InAppTextsOf(rendered.Value),
            settings.SmsEnabled,
            timeProvider.GetUtcNow()));
        return await SaveAndQueueAsync(row, cancellationToken);
    }

    /// <summary>Writes an addressed e-mail; the refusal when it is malformed or cannot be rendered.</summary>
    public async Task<Outcome<long>> WriteAsync(AddressedNotification notification, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(notification);
        if (string.IsNullOrWhiteSpace(notification.EmailAddress)
            || notification.EmailAddress.Length > NotificationsLimits.EmailMaxLength
            || string.IsNullOrWhiteSpace(notification.TemplateKey)
            || !Enum.IsDefined(notification.Language))
        {
            return Refusal.Unprocessable(NotificationsErrorCodes.InvalidNotification);
        }

        var slot = new TemplateSlot(NotificationChannels.Email, notification.Language);
        var rendered = await composer.ComposeOneAsync(notification.TemplateKey, notification.Placeholders, slot, cancellationToken);
        if (!rendered.IsSuccess)
        {
            return rendered.Refusal;
        }

        var row = Notification.ForAddress(new AddressedDraft(
            notification.EmailAddress.Trim(),
            notification.Language,
            notification.TemplateKey,
            PlaceholderJson.Write(notification.Placeholders),
            timeProvider.GetUtcNow()));
        return await SaveAndQueueAsync(row, cancellationToken);
    }

    private static bool IsWellFormed(UserNotification notification) =>
        notification.UserId > 0
        && !string.IsNullOrWhiteSpace(notification.TemplateKey)
        && notification.TemplateKey.Length <= NotificationsLimits.TemplateKeyMaxLength
        && NotificationChannelSet.IsValidSelection(notification.Channels)
        && (notification.Link is null || notification.Link.Length <= NotificationsLimits.LinkMaxLength);

    private static InAppTexts? InAppTextsOf(IReadOnlyDictionary<TemplateSlot, RenderedMessage> rendered)
    {
        var arabic = new TemplateSlot(NotificationChannels.InApp, AppLanguage.Arabic);
        var english = new TemplateSlot(NotificationChannels.InApp, AppLanguage.English);
        if (!rendered.TryGetValue(arabic, out var ar) || !rendered.TryGetValue(english, out var en))
        {
            return null;
        }

        return new InAppTexts(
            TitleAr: Fit(ar.Subject, NotificationsLimits.TitleTextMaxLength),
            TitleEn: Fit(en.Subject, NotificationsLimits.TitleTextMaxLength),
            BodyAr: Fit(ar.Body, NotificationsLimits.BodyTextMaxLength),
            BodyEn: Fit(en.Body, NotificationsLimits.BodyTextMaxLength));
    }

    private static string Fit(string text, int maxLength) => text.Length <= maxLength ? text : text[..maxLength];

    private async Task<Outcome<long>> SaveAndQueueAsync(Notification row, CancellationToken cancellationToken)
    {
        store.Add(row);
        await store.SaveChangesAsync(cancellationToken);
        foreach (var channel in new[] { NotificationChannels.Email, NotificationChannels.Sms })
        {
            if (row.StateOf(channel) != ChannelDeliveryState.Pending)
            {
                continue;
            }

            var payload = new DeliveryPayload(row.Id, channel);
            await outbox.EnqueueAsync(new OutboxMessage(DeliveryPayload.MessageTypeOf(channel), payload.ToJson(), NotBefore: null), cancellationToken);
        }

        return row.Id;
    }
}
