using Starter.Domain.Localization;

namespace Starter.Application.Notifications;

/// <summary>
/// Where to reach a user (implemented by the Identity module), so the Notifications module never references the
/// accounts tables. Read at delivery time, so a changed address or language is used by every later delivery.
/// </summary>
public interface INotificationRecipients
{
    /// <summary>The user's contact details, or null when no such user exists.</summary>
    Task<NotificationRecipient?> FindAsync(long userId, CancellationToken cancellationToken);
}

/// <summary>A user's contact details for delivery.</summary>
/// <param name="UserId">The internal user id.</param>
/// <param name="Email">The e-mail address, or null when the account has none.</param>
/// <param name="Mobile">The mobile number in international form, or null when the account has none.</param>
/// <param name="Language">The language the user reads notifications in.</param>
public sealed record NotificationRecipient(long UserId, string? Email, string? Mobile, AppLanguage Language);
