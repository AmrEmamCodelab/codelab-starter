using Starter.Application.Notifications;
using Starter.Domain.Localization;
using Starter.Modules.Notifications.Application.Rendering;

namespace Starter.Modules.Notifications.Application.Delivery;

/// <summary>One outbox-delivered channel: where it reaches a user and how a rendered text is sent on it.</summary>
public interface IDeliveryChannel
{
    /// <summary>The channel (<see cref="NotificationChannels.Email"/> or <see cref="NotificationChannels.Sms"/>).</summary>
    NotificationChannels Channel { get; }

    /// <summary>The user's address on this channel, or null when the user has none.</summary>
    string? FindAddress(NotificationRecipient recipient);

    /// <summary>Sends <paramref name="message"/> to <paramref name="target"/>.</summary>
    Task SendAsync(DeliveryTarget target, RenderedMessage message, CancellationToken cancellationToken);
}

/// <summary>Where and in which language a delivery goes.</summary>
/// <param name="Address">The e-mail address or mobile number.</param>
/// <param name="Language">The language to render.</param>
public sealed record DeliveryTarget(string Address, AppLanguage Language);

/// <summary>The e-mail channel over <see cref="IEmailSender"/>.</summary>
public sealed class EmailChannel(IEmailSender sender) : IDeliveryChannel
{
    /// <inheritdoc />
    public NotificationChannels Channel => NotificationChannels.Email;

    /// <inheritdoc />
    public string? FindAddress(NotificationRecipient recipient)
    {
        ArgumentNullException.ThrowIfNull(recipient);
        return string.IsNullOrWhiteSpace(recipient.Email) ? null : recipient.Email.Trim();
    }

    /// <inheritdoc />
    public Task SendAsync(DeliveryTarget target, RenderedMessage message, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(message);
        return sender.SendAsync(new EmailMessage(target.Address, message.Subject, message.Body), cancellationToken);
    }
}

/// <summary>The SMS channel over <see cref="ISmsSender"/>.</summary>
public sealed class SmsChannel(ISmsSender sender) : IDeliveryChannel
{
    /// <inheritdoc />
    public NotificationChannels Channel => NotificationChannels.Sms;

    /// <inheritdoc />
    public string? FindAddress(NotificationRecipient recipient)
    {
        ArgumentNullException.ThrowIfNull(recipient);
        return string.IsNullOrWhiteSpace(recipient.Mobile) ? null : recipient.Mobile.Trim();
    }

    /// <inheritdoc />
    public Task SendAsync(DeliveryTarget target, RenderedMessage message, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(message);
        return sender.SendAsync(new SmsMessage(target.Address, message.Body, target.Language), cancellationToken);
    }
}
