using Starter.Application.Notifications;
using Starter.Domain.Localization;

namespace Starter.Modules.Notifications.Domain;

/// <summary>
/// One template text: a key, one channel and one language. Seeded only by migrations (never edited at run time); the
/// key, channel and language form the primary key. Placeholders are written <c>{{name}}</c>.
/// </summary>
public sealed class NotificationTemplate
{
    private NotificationTemplate()
    {
    }

    /// <summary>The template key (a <see cref="NotificationTemplateKeys"/> constant).</summary>
    public string Key { get; private set; } = string.Empty;

    /// <summary>The single channel this text is for (one flag of <see cref="NotificationChannels"/>).</summary>
    public NotificationChannels Channel { get; private set; }

    /// <summary>The language of the text.</summary>
    public AppLanguage Language { get; private set; }

    /// <summary>The e-mail subject or bell title; null for SMS.</summary>
    public string? Subject { get; private set; }

    /// <summary>The body: HTML for e-mail, plain text for the bell and SMS.</summary>
    public string Body { get; private set; } = string.Empty;

    /// <summary>A template text (migrations and tests).</summary>
    public static NotificationTemplate Define(TemplateText text)
    {
        ArgumentNullException.ThrowIfNull(text);
        ArgumentException.ThrowIfNullOrWhiteSpace(text.Key);
        ArgumentException.ThrowIfNullOrWhiteSpace(text.Body);
        if (!NotificationChannelSet.IsSingle(text.Channel))
        {
            throw new ArgumentException("A template text belongs to exactly one channel.", nameof(text));
        }

        return new NotificationTemplate
        {
            Key = text.Key,
            Channel = text.Channel,
            Language = text.Language,
            Subject = text.Subject,
            Body = text.Body,
        };
    }
}

/// <summary>The values of one template text.</summary>
/// <param name="Key">The template key.</param>
/// <param name="Channel">One channel.</param>
/// <param name="Language">The language.</param>
/// <param name="Subject">The subject or title; null for SMS.</param>
/// <param name="Body">The body.</param>
public sealed record TemplateText(string Key, NotificationChannels Channel, AppLanguage Language, string? Subject, string Body);

/// <summary>Rules over <see cref="NotificationChannels"/> values.</summary>
public static class NotificationChannelSet
{
    /// <summary>The channels a notification can be delivered on, in delivery order.</summary>
    public static IReadOnlyList<NotificationChannels> All { get; } =
        [NotificationChannels.InApp, NotificationChannels.Email, NotificationChannels.Sms];

    /// <summary>Every flag of <see cref="All"/>.</summary>
    public static NotificationChannels Known { get; } = NotificationChannels.InApp | NotificationChannels.Email | NotificationChannels.Sms;

    /// <summary>True when <paramref name="channels"/> is exactly one known channel.</summary>
    public static bool IsSingle(NotificationChannels channels) => All.Contains(channels);

    /// <summary>True when <paramref name="channels"/> names at least one channel and no unknown flag.</summary>
    public static bool IsValidSelection(NotificationChannels channels) =>
        channels != NotificationChannels.None && (channels & ~Known) == NotificationChannels.None;

    /// <summary>The single channels in <paramref name="channels"/>, in delivery order.</summary>
    public static IReadOnlyList<NotificationChannels> Split(NotificationChannels channels) =>
        All.Where(channel => channels.HasFlag(channel)).ToList();
}
