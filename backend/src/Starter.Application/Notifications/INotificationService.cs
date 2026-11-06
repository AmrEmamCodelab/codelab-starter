using Starter.Domain.Localization;

namespace Starter.Application.Notifications;

/// <summary>
/// Sends notifications from any module (implemented by the Notifications module). A notification is a template
/// (<see cref="NotificationTemplateKeys"/>) rendered with placeholder values in the recipient's language, on one or
/// more <see cref="NotificationChannels"/>. The bell item is written at once; e-mail and SMS go through the outbox
/// (retried with back-off, delivered at least once).
/// </summary>
/// <remarks>
/// Placeholder values are stored with the notification until it is delivered: never pass a password, token, sealed
/// ticket or link that carries one to <see cref="NotifyUserAsync"/> or <see cref="SendToAddressAsync"/>. A secret-bearing
/// e-mail goes through <see cref="SendImmediateEmailAsync"/> from inside an outbox handler instead.
/// </remarks>
public interface INotificationService
{
    /// <summary>
    /// Writes the bell item now (when <see cref="NotificationChannels.InApp"/> is asked for) and queues the e-mail and
    /// SMS deliveries. SMS is skipped while <c>Notifications:Sms:Enabled</c> is false.
    /// </summary>
    /// <exception cref="Domain.Exceptions.BusinessRuleException">
    /// The template is missing for an asked channel, or a placeholder it uses has no value (a caller defect; nothing is
    /// written).
    /// </exception>
    Task NotifyUserAsync(UserNotification notification, CancellationToken cancellationToken);

    /// <summary>Queues an e-mail to an address that is not a user's (for example the contact inbox).</summary>
    /// <exception cref="Domain.Exceptions.BusinessRuleException">The template or a placeholder value is missing.</exception>
    Task SendToAddressAsync(AddressedNotification notification, CancellationToken cancellationToken);

    /// <summary>
    /// Renders the e-mail in the recipient's language and sends it now, storing nothing (the placeholder values never
    /// reach the database). For secret-bearing links; call it from an outbox handler so a failure is retried there.
    /// <see cref="UserNotification.Channels"/> and <see cref="UserNotification.Link"/> are ignored.
    /// </summary>
    /// <exception cref="Domain.Exceptions.BusinessRuleException">
    /// The user is unknown or has no e-mail address, or the template or a placeholder value is missing.
    /// </exception>
    Task SendImmediateEmailAsync(UserNotification notification, CancellationToken cancellationToken);
}

/// <summary>A notification to one user.</summary>
/// <param name="UserId">The recipient's internal user id.</param>
/// <param name="TemplateKey">A <see cref="NotificationTemplateKeys"/> constant.</param>
/// <param name="Placeholders">The values of the template's <c>{{name}}</c> placeholders (names are case-sensitive).</param>
/// <param name="Channels">Where to deliver; at least one channel.</param>
/// <param name="Link">An app-relative link the bell item opens (for example <c>/requests/abc</c>), or null.</param>
public sealed record UserNotification(
    long UserId,
    string TemplateKey,
    IReadOnlyDictionary<string, string> Placeholders,
    NotificationChannels Channels,
    string? Link);

/// <summary>An e-mail notification to an address (no user, no bell item).</summary>
/// <param name="EmailAddress">The recipient address.</param>
/// <param name="TemplateKey">A <see cref="NotificationTemplateKeys"/> constant.</param>
/// <param name="Placeholders">The values of the template's <c>{{name}}</c> placeholders.</param>
/// <param name="Language">The language to render.</param>
public sealed record AddressedNotification(
    string EmailAddress,
    string TemplateKey,
    IReadOnlyDictionary<string, string> Placeholders,
    AppLanguage Language);

/// <summary>Where a notification is delivered. Flags; stored and sent as the integer value.</summary>
[Flags]
public enum NotificationChannels
{
    /// <summary>Nowhere (never valid for a send).</summary>
    None = 0,

    /// <summary>The bell and the notifications page.</summary>
    InApp = 1,

    /// <summary>E-mail to the recipient's address.</summary>
    Email = 2,

    /// <summary>SMS to the recipient's mobile number (only while SMS is enabled).</summary>
    Sms = 4,
}
