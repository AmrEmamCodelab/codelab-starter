using Starter.Application.Notifications;
using Starter.Domain.Localization;
using Starter.Modules.Identity.Application;

namespace Starter.Modules.Identity.Infrastructure.Notifications;

/// <summary>
/// The <see cref="INotificationRecipients"/> directory over Identity's own accounts, so the Notifications module never
/// reads the <c>identity</c> tables. Read at delivery time: a changed address is used by every later delivery.
/// </summary>
/// <remarks>
/// Accounts store no mobile number and no preferred language yet, so <see cref="NotificationRecipient.Mobile"/> is
/// always null (SMS to a user ends NoAddress) and the language is <see cref="DefaultLanguage"/>. A deactivated account is
/// still a recipient: whether it should be notified is the sender's decision (the activation e-mail checks it).
/// </remarks>
public sealed class NotificationRecipients(IUserAccountRepository accounts) : INotificationRecipients
{
    /// <summary>The language of every recipient until accounts carry a preferred language (Arabic-first).</summary>
    public const AppLanguage DefaultLanguage = AppLanguage.Arabic;

    /// <inheritdoc />
    public async Task<NotificationRecipient?> FindAsync(long userId, CancellationToken cancellationToken)
    {
        var account = await accounts.FindByIdAsync(userId, cancellationToken);
        return account is null
            ? null
            : new NotificationRecipient(account.Id, Email: string.IsNullOrWhiteSpace(account.Email) ? null : account.Email, Mobile: null, DefaultLanguage);
    }
}
