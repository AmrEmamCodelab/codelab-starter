using Microsoft.Extensions.Logging;
using Starter.Application.Notifications;
using Starter.Application.Numbering;
using Starter.Modules.Portal.Domain;

namespace Starter.Modules.Portal.Application.Contact;

/// <summary>
/// Files a validated contact message: allocates its <c>CU-YYYY-NNN</c> number
/// (<see cref="PortalNumberPrefixes.ContactUs"/>) and stores it. A number is taken even when the save then fails (a
/// gap, never a duplicate: the column is unique).
/// </summary>
public sealed class ContactFiling(IRequestNumberAllocator numbers, IContactMessageStore messages, TimeProvider timeProvider)
{
    /// <summary>The stored message.</summary>
    /// <param name="input">The validated, trimmed command.</param>
    /// <param name="topic">The active topic it is filed under.</param>
    /// <param name="cancellationToken">Cancels the allocation and the save.</param>
    public async Task<ContactMessage> FileAsync(SubmitContactMessageCommand input, ContactTopic topic, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(input);
        ArgumentNullException.ThrowIfNull(topic);
        var language = ContactLanguageCodes.Find(input.Language)
            ?? throw new ArgumentException("The command is not validated: the language is not ar or en.", nameof(input));
        var message = ContactMessage.Receive(new NewContactMessage(
            ReferenceNumber: await numbers.NextAsync(PortalNumberPrefixes.ContactUs, cancellationToken),
            FullName: input.FullName!,
            Email: input.Email!,
            TopicId: topic.Id,
            Message: input.Message!,
            Language: language,
            ReceivedAt: timeProvider.GetUtcNow()));
        messages.Add(message);
        await messages.SaveChangesAsync(cancellationToken);
        return message;
    }
}

/// <summary>
/// Announces a stored contact message to the contact inbox (<see cref="ContactInbox"/>, template
/// <see cref="NotificationTemplateKeys.ContactReceived"/>, topic name in the inbox language). The e-mail goes through
/// the outbox, which retries delivery.
/// </summary>
/// <remarks>
/// A post-commit side effect (ERROR-HANDLING row 8): the message is already stored, so a failure to queue the e-mail is
/// logged with the reference number only (never the sender's details) and the sender still gets the receipt; a retry
/// by the sender would file the message twice.
/// </remarks>
public sealed partial class ContactAnnouncer(INotificationService notifications, ContactInbox inbox, ILogger<ContactAnnouncer> logger)
{
    /// <summary>Queues the inbox e-mail; never throws for a notification failure (cancellation propagates).</summary>
    public async Task AnnounceAsync(ContactMessage message, ContactTopic topic, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(message);
        ArgumentNullException.ThrowIfNull(topic);
        try
        {
            await notifications.SendToAddressAsync(
                new AddressedNotification(inbox.EmailAddress, NotificationTemplateKeys.ContactReceived, Placeholders(message, topic), inbox.Language),
                cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            // Load-bearing (ERROR-HANDLING row 8): the message is stored; the sender keeps the receipt.
            LogAnnounceFailed(logger, exception, message.ReferenceNumber);
        }
    }

    private Dictionary<string, string> Placeholders(ContactMessage message, ContactTopic topic) => new(StringComparer.Ordinal)
    {
        [NotificationPlaceholders.ReferenceNumber] = message.ReferenceNumber,
        [NotificationPlaceholders.FullName] = message.FullName,
        [NotificationPlaceholders.Email] = message.Email,
        [NotificationPlaceholders.Topic] = topic.NameIn(inbox.Language),
        [NotificationPlaceholders.Message] = message.Message,
    };

    [LoggerMessage(EventId = 1501, Level = LogLevel.Error, Message = "Contact message {ReferenceNumber} was stored, but its inbox notification could not be queued")]
    private static partial void LogAnnounceFailed(ILogger logger, Exception exception, string referenceNumber);
}
