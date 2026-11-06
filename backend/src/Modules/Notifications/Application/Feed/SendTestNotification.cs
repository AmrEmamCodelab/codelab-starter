using System.Globalization;
using Starter.Application.Notifications;
using Starter.Application.Results;
using Starter.Modules.Notifications.Application.Delivery;

namespace Starter.Modules.Notifications.Application.Feed;

/// <summary>
/// Sends the caller the test notification on every channel (<see cref="NotificationTemplateKeys.TestNotification"/>):
/// the bell item at once, the e-mail and (while SMS is enabled) the SMS through the outbox. It checks the whole path an
/// administrator cares about: templates, bell, outbox, senders.
/// </summary>
public sealed class SendTestNotification(CurrentFeedOwner owners, NotificationWriter writer, TimeProvider timeProvider)
{
    /// <summary>The display format of the send time (UTC).</summary>
    public const string SentAtFormat = "yyyy-MM-dd HH:mm 'UTC'";

    /// <summary>Every channel; the writer drops SMS while it is disabled.</summary>
    public const NotificationChannels Channels = NotificationChannels.InApp | NotificationChannels.Email | NotificationChannels.Sms;

    /// <summary>Done, or <see cref="NotificationsErrorCodes.RecipientNotFound"/> when the caller has no user account.</summary>
    public async Task<Outcome<Done>> ExecuteAsync(CancellationToken cancellationToken)
    {
        if (await owners.FindAsync(cancellationToken) is not { } owner)
        {
            return Refusal.NotFound(NotificationsErrorCodes.RecipientNotFound);
        }

        var sentAt = timeProvider.GetUtcNow().ToString(SentAtFormat, CultureInfo.InvariantCulture);
        var placeholders = new Dictionary<string, string>(StringComparer.Ordinal) { [NotificationPlaceholders.SentAt] = sentAt };
        var written = await writer.WriteAsync(
            new UserNotification(owner.UserId, NotificationTemplateKeys.TestNotification, placeholders, Channels, Link: null),
            cancellationToken);
        return written.IsSuccess ? Done.Value : written.Refusal;
    }
}
