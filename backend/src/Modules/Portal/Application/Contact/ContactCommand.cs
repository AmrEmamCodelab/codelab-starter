using Starter.Application.Validation;
using Starter.Domain.Localization;
using Starter.Modules.Portal.Domain;

namespace Starter.Modules.Portal.Application.Contact;

/// <summary>
/// The body of <c>POST /api/portal/contact-messages</c>. Every member is nullable, so a missing field comes back as a
/// field error of the validator rather than as a binding failure in another shape.
/// </summary>
/// <param name="FullName">The sender's name (1-100 characters after trimming).</param>
/// <param name="Email">The sender's e-mail address.</param>
/// <param name="TopicId">The contact topic: an active <c>contact-topics</c> lookup id (<c>ContactTopicIds</c>).</param>
/// <param name="Message">The message (10-2000 characters after trimming).</param>
/// <param name="Language">The language to reply in: <c>ar</c> or <c>en</c> (<see cref="ContactLanguageCodes"/>).</param>
public sealed record SubmitContactMessageCommand(string? FullName, string? Email, int? TopicId, string? Message, string? Language)
{
    /// <summary>The same body with surrounding white space removed from every text, so blank reads as missing.</summary>
    public SubmitContactMessageCommand Trimmed() => this with
    {
        FullName = FullName?.Trim(),
        Email = Email?.Trim(),
        Message = Message?.Trim(),
        Language = Language?.Trim(),
    };
}

/// <summary>
/// The language codes of the contact form's <c>language</c> field. They are the BCP 47 primary subtags the host uses
/// (<c>Starter.Infrastructure.Localization.LanguageTags</c>; a test keeps the two equal).
/// </summary>
public static class ContactLanguageCodes
{
    /// <summary>Arabic.</summary>
    public const string Arabic = "ar";

    /// <summary>English.</summary>
    public const string English = "en";

    /// <summary>The language of <paramref name="code"/> (case-insensitive), or null for anything else.</summary>
    public static AppLanguage? Find(string? code) =>
        string.Equals(code, Arabic, StringComparison.OrdinalIgnoreCase) ? AppLanguage.Arabic
        : string.Equals(code, English, StringComparison.OrdinalIgnoreCase) ? AppLanguage.English
        : null;
}

/// <summary>
/// Validates <see cref="SubmitContactMessageCommand"/> on trimmed values, every field error at once: the form's own
/// rules (<see cref="PortalLimits"/>), enforced again for callers that skip the form. Whether the topic is active is
/// checked by the use case (it needs the lookup data).
/// </summary>
public sealed class SubmitContactMessageValidator : IValidator<SubmitContactMessageCommand>
{
    /// <inheritdoc />
    public ValidationErrors Validate(SubmitContactMessageCommand input)
    {
        ArgumentNullException.ThrowIfNull(input);
        var trimmed = input.Trimmed();
        var errors = new ValidationErrors()
            .RequireText(FieldNames.Of(nameof(SubmitContactMessageCommand.FullName)), trimmed.FullName, PortalLimits.FullNameMaxLength)
            .RequireText(FieldNames.Of(nameof(SubmitContactMessageCommand.Email)), trimmed.Email, PortalLimits.EmailMaxLength)
            .RequireText(FieldNames.Of(nameof(SubmitContactMessageCommand.Message)), trimmed.Message, PortalLimits.MessageMaxLength);
        CheckEmailShape(errors, trimmed.Email);
        CheckMessageLength(errors, trimmed.Message);
        CheckTopic(errors, trimmed.TopicId);
        CheckLanguage(errors, trimmed.Language);
        return errors;
    }

    private static void CheckEmailShape(ValidationErrors errors, string? email)
    {
        if (!string.IsNullOrEmpty(email) && email.Length <= PortalLimits.EmailMaxLength && !EmailShape.IsWellFormed(email))
        {
            errors.Add(field: FieldNames.Of(nameof(SubmitContactMessageCommand.Email)), message: ValidationMessages.InvalidFormat);
        }
    }

    private static void CheckMessageLength(ValidationErrors errors, string? message)
    {
        if (!string.IsNullOrEmpty(message) && message.Length < PortalLimits.MessageMinLength)
        {
            errors.Add(field: FieldNames.Of(nameof(SubmitContactMessageCommand.Message)), message: PortalValidationMessages.TooShort);
        }
    }

    private static void CheckTopic(ValidationErrors errors, int? topicId)
    {
        var field = FieldNames.Of(nameof(SubmitContactMessageCommand.TopicId));
        if (topicId is null)
        {
            errors.Add(field: field, message: ValidationMessages.Required);
        }
        else if (topicId < 1)
        {
            errors.Add(field: field, message: ValidationMessages.InvalidFormat);
        }
    }

    private static void CheckLanguage(ValidationErrors errors, string? language)
    {
        var field = FieldNames.Of(nameof(SubmitContactMessageCommand.Language));
        if (string.IsNullOrEmpty(language))
        {
            errors.Add(field: field, message: ValidationMessages.Required);
        }
        else if (ContactLanguageCodes.Find(language) is null)
        {
            errors.Add(field: field, message: ValidationMessages.InvalidFormat);
        }
    }
}
