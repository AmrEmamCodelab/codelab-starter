using System.Globalization;
using Microsoft.Extensions.Logging;
using Starter.Application.Messaging;
using Starter.Application.Notifications;
using Starter.Application.Users;
using Starter.Domain.Exceptions;

namespace Starter.Modules.Users.Application.Accounts;

/// <summary>
/// Delivers <see cref="OutboxMessageTypes.AccountActivation"/> messages: at drain time it asks Identity for a fresh
/// sealed ticket (through <see cref="IAccountAdministration.IssueActivationAsync"/>), builds the link with
/// <see cref="ActivationLinkSettings"/> and e-mails it at once through
/// <see cref="INotificationService.SendImmediateEmailAsync"/> (template
/// <see cref="NotificationTemplateKeys.AccountActivation"/>, in the recipient's language). The ticket exists only in
/// that e-mail: never in the payload, a table or a log line.
/// </summary>
/// <remarks>
/// <para><b>Supersede.</b> Each issue supersedes every older link of the account, so the newest e-mailed link is the
/// valid one; one-time use stays enforced by Identity when the link is redeemed.</para>
/// <para><b>Idempotence.</b> A message that is no longer due (<see cref="ActivationDeliveryRules.SkipReasonOf"/>: the
/// password was set, the account deactivated, rebound or without an address) is completed without an e-mail, so a
/// redelivery after the user activated never clears their password. A redelivery before that (a crash between the send
/// and the outbox's Sent mark) sends one more e-mail whose link supersedes the first; delivery is at least once.</para>
/// <para><b>Failures.</b> An unreadable payload is logged and completed (a retry cannot fix it). A missing link base,
/// an Identity refusal or a send failure throws, so the outbox retries with back-off and finally marks it Failed.</para>
/// </remarks>
public sealed partial class AccountActivationEmailHandler(
    IAccountAdministration accounts,
    INotificationService notifications,
    IUserPublicIds publicIds,
    ActivationLinkSettings links,
    ILogger<AccountActivationEmailHandler> logger) : IOutboxHandler
{
    /// <summary>How the expiry is written into the e-mail (the <see cref="NotificationPlaceholders.ExpiresAt"/> value).</summary>
    public const string ExpiresAtFormat = "yyyy-MM-dd HH:mm 'UTC'";

    /// <inheritdoc />
    public string MessageType => OutboxMessageTypes.AccountActivation;

    /// <inheritdoc />
    public async Task HandleAsync(OutboxDelivery delivery, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(delivery);
        var payload = ActivationPayloads.TryRead(delivery.PayloadJson);
        if (payload is null || publicIds.IdOf(payload.UserPublicId) is not { } userId)
        {
            LogUnreadablePayload(logger, delivery.MessageId);
            return;
        }

        var account = await accounts.FindAsync(userId, cancellationToken);
        if (ActivationDeliveryRules.SkipReasonOf(account) is { } skip)
        {
            LogSkipped(logger, delivery.MessageId, payload.TicketId, (int)skip);
            return;
        }

        await SendFreshLinkAsync(userId, delivery.MessageId, cancellationToken);
    }

    /// <summary>The placeholder values of the activation e-mail.</summary>
    public static IReadOnlyDictionary<string, string> PlaceholdersOf(string activationLink, DateTimeOffset expiresAt) =>
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            [NotificationPlaceholders.ActivationLink] = activationLink,
            [NotificationPlaceholders.ExpiresAt] = expiresAt.UtcDateTime.ToString(ExpiresAtFormat, CultureInfo.InvariantCulture),
        };

    private async Task SendFreshLinkAsync(long userId, long messageId, CancellationToken cancellationToken)
    {
        if (!links.IsConfigured)
        {
            throw new InvalidOperationException("Users:Activation:LinkBaseUrl is not configured; the activation e-mail cannot be built.");
        }

        var issued = await accounts.IssueActivationAsync(userId, cancellationToken);
        if (!issued.IsSuccess)
        {
            throw new BusinessRuleException(issued.Refusal.Code, $"Identity refused the activation of outbox message {messageId} ({issued.Refusal.Code}).");
        }

        var activation = issued.Value;
        await notifications.SendImmediateEmailAsync(
            new UserNotification(
                UserId: userId,
                TemplateKey: NotificationTemplateKeys.AccountActivation,
                Placeholders: PlaceholdersOf(links.LinkFor(activation.Ticket), activation.ExpiresAt),
                Channels: NotificationChannels.Email,
                Link: null),
            cancellationToken);
        LogSent(logger, messageId, activation.TicketId);
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Outbox message {MessageId} has no readable account-activation payload; nothing sent.")]
    private static partial void LogUnreadablePayload(ILogger logger, long messageId);

    [LoggerMessage(Level = LogLevel.Information, Message = "Outbox message {MessageId} (ticket id {TicketId}) is no longer due (reason {SkipReason}); no activation e-mail sent.")]
    private static partial void LogSkipped(ILogger logger, long messageId, string ticketId, int skipReason);

    [LoggerMessage(Level = LogLevel.Information, Message = "Outbox message {MessageId}: activation e-mail sent with the fresh ticket id {TicketId}.")]
    private static partial void LogSent(ILogger logger, long messageId, string ticketId);
}
