using Starter.Application.Notifications;
using Starter.Application.Results;
using Starter.Domain.Localization;
using Starter.Modules.Notifications.Domain;

namespace Starter.Modules.Notifications.Application.Rendering;

/// <summary>One channel in one language.</summary>
/// <param name="Channel">A single channel.</param>
/// <param name="Language">The language.</param>
public sealed record TemplateSlot(NotificationChannels Channel, AppLanguage Language);

/// <summary>A rendered text.</summary>
/// <param name="Subject">The e-mail subject or bell title (one line); empty for SMS.</param>
/// <param name="Body">The body: HTML for e-mail, plain text otherwise.</param>
public sealed record RenderedMessage(string Subject, string Body);

/// <summary>
/// Renders a template for one or more <see cref="TemplateSlot"/>s from its seeded texts: one store read per call. The
/// encoding follows the channel (e-mail body HTML-encoded, every subject and title on one line, the bell and SMS bodies
/// plain). The first missing text or placeholder refuses the whole call.
/// </summary>
public sealed class NotificationComposer(ITemplateStore templates)
{
    /// <summary>Every language the product renders.</summary>
    public static IReadOnlyList<AppLanguage> Languages { get; } = [AppLanguage.Arabic, AppLanguage.English];

    /// <summary>The rendered texts keyed by slot, or the first refusal.</summary>
    public async Task<Outcome<IReadOnlyDictionary<TemplateSlot, RenderedMessage>>> ComposeAsync(
        string templateKey,
        IReadOnlyDictionary<string, string> placeholders,
        IReadOnlyCollection<TemplateSlot> slots,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(slots);
        var texts = await templates.ListAsync(templateKey, cancellationToken);
        var rendered = new Dictionary<TemplateSlot, RenderedMessage>();
        foreach (var slot in slots)
        {
            var outcome = RenderSlot(texts, slot, placeholders);
            if (!outcome.IsSuccess)
            {
                return outcome.Refusal;
            }

            rendered[slot] = outcome.Value;
        }

        return rendered;
    }

    /// <summary>One rendered text, or the refusal.</summary>
    public async Task<Outcome<RenderedMessage>> ComposeOneAsync(
        string templateKey,
        IReadOnlyDictionary<string, string> placeholders,
        TemplateSlot slot,
        CancellationToken cancellationToken)
    {
        var outcome = await ComposeAsync(templateKey, placeholders, [slot], cancellationToken);
        return outcome.IsSuccess ? outcome.Value[slot] : outcome.Refusal;
    }

    /// <summary>The slots of <paramref name="channels"/> in every language.</summary>
    public static IReadOnlyList<TemplateSlot> SlotsFor(IEnumerable<NotificationChannels> channels) =>
        channels.SelectMany(channel => Languages.Select(language => new TemplateSlot(channel, language))).ToList();

    private static Outcome<RenderedMessage> RenderSlot(
        IReadOnlyList<NotificationTemplate> texts,
        TemplateSlot slot,
        IReadOnlyDictionary<string, string> placeholders)
    {
        var text = texts.FirstOrDefault(candidate => candidate.Channel == slot.Channel && candidate.Language == slot.Language);
        if (text is null)
        {
            return Refusal.Unprocessable(NotificationsErrorCodes.TemplateNotFound);
        }

        var subject = slot.Channel == NotificationChannels.Sms
            ? Outcome.Success(string.Empty)
            : TemplateRenderer.Render(text.Subject ?? string.Empty, placeholders, TextEncoding.SingleLine);
        var bodyEncoding = slot.Channel == NotificationChannels.Email ? TextEncoding.Html : TextEncoding.PlainText;
        var body = TemplateRenderer.Render(text.Body, placeholders, bodyEncoding);
        if (!subject.IsSuccess)
        {
            return subject.Refusal;
        }

        return body.IsSuccess ? new RenderedMessage(subject.Value, body.Value) : body.Refusal;
    }
}
