using Starter.Domain.Localization;
using Starter.Modules.Notifications.Domain;

namespace Starter.Modules.Notifications.Application;

/// <summary>The notifications (implemented by the module's EF Core context in Infrastructure).</summary>
public interface INotificationStore
{
    /// <summary>Stages a new notification (written by <see cref="SaveChangesAsync"/>).</summary>
    void Add(Notification notification);

    /// <summary>The notification, or null.</summary>
    Task<Notification?> FindAsync(long id, CancellationToken cancellationToken);

    /// <summary>The user's bell item with this id, or null when it does not exist, is another user's or has no in-app channel.</summary>
    Task<Notification?> FindInAppAsync(FeedOwner owner, long id, CancellationToken cancellationToken);

    /// <summary>The user's bell items older than <see cref="FeedQuery.BeforeId"/>, newest first, at most <see cref="FeedQuery.Take"/>.</summary>
    Task<IReadOnlyList<Notification>> ListInAppAsync(FeedQuery query, CancellationToken cancellationToken);

    /// <summary>How many of the user's bell items are unread.</summary>
    Task<int> CountUnreadAsync(FeedOwner owner, CancellationToken cancellationToken);

    /// <summary>Writes every staged change.</summary>
    Task SaveChangesAsync(CancellationToken cancellationToken);
}

/// <summary>The user whose bell is read (an internal user id, never from the wire).</summary>
/// <param name="UserId">The internal user id.</param>
public sealed record FeedOwner(long UserId);

/// <summary>One feed page request.</summary>
/// <param name="Owner">Whose bell.</param>
/// <param name="BeforeId">Only items with a smaller id (older), or null for the newest.</param>
/// <param name="Take">How many items at most.</param>
public sealed record FeedQuery(FeedOwner Owner, long? BeforeId, int Take);

/// <summary>The template texts (seeded by migrations).</summary>
public interface ITemplateStore
{
    /// <summary>Every text of the template <paramref name="key"/> (all channels and languages); empty when unknown.</summary>
    Task<IReadOnlyList<NotificationTemplate>> ListAsync(string key, CancellationToken cancellationToken);
}

/// <summary>
/// Sends one e-mail (module-internal transport port; knows nothing about templates). Returns only when the relay or
/// the pickup folder accepted the message; throws otherwise, so the outbox retries.
/// </summary>
public interface IEmailSender
{
    /// <summary>Sends <paramref name="message"/>.</summary>
    Task SendAsync(EmailMessage message, CancellationToken cancellationToken);
}

/// <summary>One e-mail. The sender address comes from the options.</summary>
/// <param name="To">The recipient address.</param>
/// <param name="Subject">The subject (one line).</param>
/// <param name="HtmlBody">The HTML body (placeholder values already encoded).</param>
public sealed record EmailMessage(string To, string Subject, string HtmlBody);

/// <summary>
/// Sends one SMS (module-internal transport port). Returns only when the sink accepted the message; throws otherwise.
/// </summary>
public interface ISmsSender
{
    /// <summary>Sends <paramref name="message"/>.</summary>
    Task SendAsync(SmsMessage message, CancellationToken cancellationToken);
}

/// <summary>One SMS.</summary>
/// <param name="To">The mobile number in international form.</param>
/// <param name="Text">The text (plain, in <paramref name="Language"/>).</param>
/// <param name="Language">The language of the text.</param>
public sealed record SmsMessage(string To, string Text, AppLanguage Language);

/// <summary>The delivery switches the Application layer needs (from <c>Notifications</c> options).</summary>
/// <param name="SmsEnabled">Whether SMS is delivered (<c>Notifications:Sms:Enabled</c>).</param>
public sealed record DeliverySettings(bool SmsEnabled);
