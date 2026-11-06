using Starter.Domain.Auditing;
using Starter.Domain.Localization;

namespace Starter.Modules.Portal.Domain;

/// <summary>
/// One message sent through the public contact form, filed under its reference number. Written once and never
/// edited. The sender's name, address and text are personal data: the audit trail keeps only their masked form.
/// </summary>
public sealed class ContactMessage
{
    private ContactMessage()
    {
    }

    /// <summary>The internal id (never on the wire: the sender only ever sees the reference number).</summary>
    public long Id { get; private set; }

    /// <summary>The reference number, <c>CU-YYYY-NNN</c> (unique).</summary>
    public string ReferenceNumber { get; private set; } = string.Empty;

    /// <summary>The sender's name, trimmed.</summary>
    [PersonalData(PersonalDataKind.Name)]
    public string FullName { get; private set; } = string.Empty;

    /// <summary>The sender's e-mail address, trimmed.</summary>
    [PersonalData(PersonalDataKind.Email)]
    public string Email { get; private set; } = string.Empty;

    /// <summary>The contact topic: an id of the <c>contact-topics</c> lookup (<c>ContactTopicIds</c>).</summary>
    public int TopicId { get; private set; }

    /// <summary>The message text, trimmed.</summary>
    [PersonalData(PersonalDataKind.FreeText)]
    public string Message { get; private set; } = string.Empty;

    /// <summary>The language the sender wrote in and wants the reply in.</summary>
    public AppLanguage Language { get; private set; }

    /// <summary>When it was received.</summary>
    public DateTimeOffset ReceivedAt { get; private set; }

    /// <summary>A new message from validated, trimmed values.</summary>
    public static ContactMessage Receive(NewContactMessage values)
    {
        ArgumentNullException.ThrowIfNull(values);
        ArgumentException.ThrowIfNullOrWhiteSpace(values.ReferenceNumber);
        ArgumentException.ThrowIfNullOrWhiteSpace(values.FullName);
        ArgumentException.ThrowIfNullOrWhiteSpace(values.Email);
        ArgumentException.ThrowIfNullOrWhiteSpace(values.Message);
        return new ContactMessage
        {
            ReferenceNumber = values.ReferenceNumber,
            FullName = values.FullName,
            Email = values.Email,
            TopicId = values.TopicId,
            Message = values.Message,
            Language = values.Language,
            ReceivedAt = values.ReceivedAt,
        };
    }
}

/// <summary>The values of a new <see cref="ContactMessage"/> (validated and trimmed by the caller).</summary>
/// <param name="ReferenceNumber">The allocated reference number.</param>
/// <param name="FullName">The sender's name.</param>
/// <param name="Email">The sender's e-mail address.</param>
/// <param name="TopicId">The contact topic id.</param>
/// <param name="Message">The message text.</param>
/// <param name="Language">The sender's language.</param>
/// <param name="ReceivedAt">When it was received.</param>
public sealed record NewContactMessage(
    string ReferenceNumber,
    string FullName,
    string Email,
    int TopicId,
    string Message,
    AppLanguage Language,
    DateTimeOffset ReceivedAt);
