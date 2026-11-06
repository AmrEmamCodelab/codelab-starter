using Starter.Application.Notifications;
using Starter.Application.Results;
using Starter.Domain.Exceptions;
using Starter.Modules.Notifications.Application.Rendering;

namespace Starter.Modules.Notifications.Application.Delivery;

/// <summary>
/// The kernel <see cref="INotificationService"/>: queued notifications through <see cref="NotificationWriter"/>, the
/// immediate e-mail through <see cref="ImmediateEmails"/>. A refusal (missing text or placeholder, malformed
/// notification) is a caller defect here and is thrown as a <see cref="BusinessRuleException"/> with its code.
/// </summary>
public sealed class NotificationService(NotificationWriter writer, ImmediateEmails immediateEmails) : INotificationService
{
    /// <inheritdoc />
    public async Task NotifyUserAsync(UserNotification notification, CancellationToken cancellationToken) =>
        ThrowIfRefused(await writer.WriteAsync(notification, cancellationToken));

    /// <inheritdoc />
    public async Task SendToAddressAsync(AddressedNotification notification, CancellationToken cancellationToken) =>
        ThrowIfRefused(await writer.WriteAsync(notification, cancellationToken));

    /// <inheritdoc />
    public async Task SendImmediateEmailAsync(UserNotification notification, CancellationToken cancellationToken) =>
        ThrowIfRefused(await immediateEmails.SendAsync(notification, cancellationToken));

    private static void ThrowIfRefused<T>(Outcome<T> outcome)
    {
        if (!outcome.IsSuccess)
        {
            throw new BusinessRuleException(outcome.Refusal.Code, $"The notification was refused ({outcome.Refusal.Code}).");
        }
    }
}

/// <summary>
/// Renders an e-mail in the recipient's language and sends it now; nothing is stored, so a secret-bearing link never
/// reaches the database or the outbox. The caller is an outbox handler: a send failure propagates and is retried there.
/// </summary>
public sealed class ImmediateEmails(NotificationComposer composer, INotificationRecipients recipients, EmailChannel channel)
{
    /// <summary>Sends the e-mail; the refusal when the user, the address, the text or a placeholder is missing.</summary>
    public async Task<Outcome<Done>> SendAsync(UserNotification notification, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(notification);
        var recipient = await recipients.FindAsync(notification.UserId, cancellationToken);
        if (recipient is null)
        {
            return Refusal.Unprocessable(NotificationsErrorCodes.RecipientNotFound);
        }

        if (channel.FindAddress(recipient) is not { } address)
        {
            return Refusal.Unprocessable(NotificationsErrorCodes.RecipientHasNoEmail);
        }

        var slot = new TemplateSlot(channel.Channel, recipient.Language);
        var rendered = await composer.ComposeOneAsync(notification.TemplateKey, notification.Placeholders, slot, cancellationToken);
        if (!rendered.IsSuccess)
        {
            return rendered.Refusal;
        }

        await channel.SendAsync(new DeliveryTarget(address, recipient.Language), rendered.Value, cancellationToken);
        return Done.Value;
    }
}
