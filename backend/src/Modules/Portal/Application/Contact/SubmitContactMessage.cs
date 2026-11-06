using Starter.Application.Results;
using Starter.Application.Settings;
using Starter.Application.Validation;

namespace Starter.Modules.Portal.Application.Contact;

/// <summary>The answer to a stored contact message: only the number, nothing the sender typed is echoed back.</summary>
/// <param name="ReferenceNumber">The reference number, <c>CU-YYYY-NNN</c>.</param>
public sealed record ContactReceipt(string ReferenceNumber);

/// <summary>
/// <c>POST /api/portal/contact-messages</c> (anonymous, rate-limited): refuses while an administrator has switched the
/// form off (<see cref="SettingKeys.PortalContactEnabled"/>), validates the message, checks its topic is an
/// active contact topic, files it under a new <c>CU-YYYY-NNN</c> number (<see cref="ContactFiling"/>), and only once
/// it is stored announces it to the contact inbox (<see cref="ContactAnnouncer"/>).
/// </summary>
public sealed class SubmitContactMessage(
    IRuntimeSettings settings,
    IValidator<SubmitContactMessageCommand> validator,
    IContactTopics topics,
    ContactFiling filing,
    ContactAnnouncer announcer)
{
    /// <summary>
    /// The receipt, or a refusal: <see cref="PortalErrorCodes.ContactDisabled"/> (409) while the
    /// <see cref="SettingKeys.PortalContactEnabled"/> runtime setting is false, <see cref="PlatformErrorCodes.Validation"/> with the field errors (400), or
    /// <see cref="PortalErrorCodes.TopicNotFound"/> on the <c>topicId</c> field (400) for an unknown or retired topic.
    /// Nothing is numbered or stored on a refusal.
    /// </summary>
    public async Task<Outcome<ContactReceipt>> ExecuteAsync(SubmitContactMessageCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        if (!settings.GetBoolean(SettingKeys.PortalContactEnabled))
        {
            return Refusal.Conflict(PortalErrorCodes.ContactDisabled);
        }

        var errors = validator.Validate(command);
        if (!errors.IsValid)
        {
            return Refusal.Invalid(errors);
        }

        var input = command.Trimmed();
        if (await topics.FindActiveAsync(input.TopicId!.Value, cancellationToken) is not { } topic)
        {
            return TopicRefusal();
        }

        var message = await filing.FileAsync(input, topic, cancellationToken);
        await announcer.AnnounceAsync(message, topic, cancellationToken);
        return new ContactReceipt(message.ReferenceNumber);
    }

    private static Refusal TopicRefusal() =>
        Refusal.Invalid(
            new ValidationErrors()
                .Add(field: FieldNames.Of(nameof(SubmitContactMessageCommand.TopicId)), message: PortalErrorCodes.TopicNotFound)
                .ToDictionary(),
            PortalErrorCodes.TopicNotFound);
}
