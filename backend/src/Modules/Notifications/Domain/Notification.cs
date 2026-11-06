using Starter.Application.Notifications;
using Starter.Domain.Auditing;
using Starter.Domain.Localization;

namespace Starter.Modules.Notifications.Domain;

/// <summary>
/// One notification: the bell item of a user (rendered at once in Arabic and English) and the delivery state of its
/// e-mail and SMS channels, or an e-mail to an address that is not a user's. Kept out of the audit trail: the table is
/// a delivery trail of its own and carries personal data (addresses, placeholder values).
/// </summary>
[NotAudited]
public sealed class Notification
{
    private Notification()
    {
    }

    /// <summary>The internal id (the wire carries its PublicId).</summary>
    public long Id { get; private set; }

    /// <summary>The recipient user, or null for an addressed e-mail.</summary>
    public long? UserId { get; private set; }

    /// <summary>The address of an addressed e-mail, or null for a user's notification.</summary>
    [PersonalData(PersonalDataKind.Email)]
    public string? EmailAddress { get; private set; }

    /// <summary>The language of an addressed e-mail; null for a user's notification (the recipient's language is read at delivery).</summary>
    public AppLanguage? Language { get; private set; }

    /// <summary>The template key.</summary>
    public string TemplateKey { get; private set; } = string.Empty;

    /// <summary>The placeholder values as JSON while an e-mail or SMS waits for delivery; null otherwise.</summary>
    [PersonalData(PersonalDataKind.FreeText)]
    public string? PlaceholdersJson { get; private set; }

    /// <summary>The app-relative link the bell item opens, or null.</summary>
    public string? Link { get; private set; }

    /// <summary>The channels asked for.</summary>
    public NotificationChannels Channels { get; private set; }

    /// <summary>The Arabic bell title, or null without the in-app channel.</summary>
    public string? TitleAr { get; private set; }

    /// <summary>The English bell title, or null without the in-app channel.</summary>
    public string? TitleEn { get; private set; }

    /// <summary>The Arabic bell body, or null without the in-app channel.</summary>
    public string? BodyAr { get; private set; }

    /// <summary>The English bell body, or null without the in-app channel.</summary>
    public string? BodyEn { get; private set; }

    /// <summary>When it was created.</summary>
    public DateTimeOffset CreatedAt { get; private set; }

    /// <summary>When the recipient marked it read, or null while unread.</summary>
    public DateTimeOffset? ReadAt { get; private set; }

    /// <summary>The e-mail channel's state.</summary>
    public ChannelDeliveryState EmailState { get; private set; } = ChannelDeliveryState.NotRequested;

    /// <summary>The SMS channel's state.</summary>
    public ChannelDeliveryState SmsState { get; private set; } = ChannelDeliveryState.NotRequested;

    /// <summary>When the e-mail was handed to the sender, or null.</summary>
    public DateTimeOffset? EmailSentAt { get; private set; }

    /// <summary>When the SMS was handed to the sender, or null.</summary>
    public DateTimeOffset? SmsSentAt { get; private set; }

    /// <summary>True while the recipient has not read it.</summary>
    public bool IsUnread => ReadAt is null;

    /// <summary>True when it shows in the bell.</summary>
    public bool IsInApp => Channels.HasFlag(NotificationChannels.InApp);

    /// <summary>A user's notification. The caller rendered the bell texts and validated every channel's template.</summary>
    public static Notification ForUser(UserNotificationDraft draft)
    {
        ArgumentNullException.ThrowIfNull(draft);
        var notification = new Notification
        {
            UserId = draft.UserId,
            TemplateKey = draft.TemplateKey,
            Link = draft.Link,
            Channels = draft.Channels,
            CreatedAt = draft.CreatedAt,
        };
        notification.ApplyInApp(draft.InApp);
        notification.EmailState = draft.Channels.HasFlag(NotificationChannels.Email) ? ChannelDeliveryState.Pending : ChannelDeliveryState.NotRequested;
        notification.SmsState = SmsStateFor(draft.Channels, draft.SmsEnabled);
        notification.PlaceholdersJson = notification.HasPendingDelivery ? draft.PlaceholdersJson : null;
        return notification;
    }

    /// <summary>An e-mail to an address (no user, no bell item).</summary>
    public static Notification ForAddress(AddressedDraft draft)
    {
        ArgumentNullException.ThrowIfNull(draft);
        return new Notification
        {
            EmailAddress = draft.EmailAddress,
            Language = draft.Language,
            TemplateKey = draft.TemplateKey,
            PlaceholdersJson = draft.PlaceholdersJson,
            Channels = NotificationChannels.Email,
            EmailState = ChannelDeliveryState.Pending,
            CreatedAt = draft.CreatedAt,
        };
    }

    /// <summary>The bell title in <paramref name="language"/>.</summary>
    public string TitleIn(AppLanguage language) => (language == AppLanguage.English ? TitleEn : TitleAr) ?? string.Empty;

    /// <summary>The bell body in <paramref name="language"/>.</summary>
    public string BodyIn(AppLanguage language) => (language == AppLanguage.English ? BodyEn : BodyAr) ?? string.Empty;

    /// <summary>Marks it read; true when it was unread (marking twice changes nothing).</summary>
    public bool MarkRead(DateTimeOffset now)
    {
        if (!IsUnread)
        {
            return false;
        }

        ReadAt = now;
        return true;
    }

    /// <summary>The state of <paramref name="channel"/> (e-mail or SMS).</summary>
    public ChannelDeliveryState StateOf(NotificationChannels channel) => channel switch
    {
        NotificationChannels.Email => EmailState,
        NotificationChannels.Sms => SmsState,
        _ => throw new ArgumentOutOfRangeException(nameof(channel), channel, "Only e-mail and SMS are delivered through the outbox."),
    };

    /// <summary>Records the outcome of a delivery of <paramref name="channel"/>; drops the placeholder values once nothing waits.</summary>
    public void Complete(NotificationChannels channel, ChannelDeliveryState outcome, DateTimeOffset now)
    {
        if (outcome is not (ChannelDeliveryState.Sent or ChannelDeliveryState.NoAddress))
        {
            throw new ArgumentOutOfRangeException(nameof(outcome), outcome, "A delivery ends Sent or NoAddress.");
        }

        var sentAt = outcome == ChannelDeliveryState.Sent ? now : (DateTimeOffset?)null;
        if (channel == NotificationChannels.Email)
        {
            EmailState = outcome;
            EmailSentAt = sentAt;
        }
        else if (channel == NotificationChannels.Sms)
        {
            SmsState = outcome;
            SmsSentAt = sentAt;
        }
        else
        {
            throw new ArgumentOutOfRangeException(nameof(channel), channel, "Only e-mail and SMS are delivered through the outbox.");
        }

        PlaceholdersJson = HasPendingDelivery ? PlaceholdersJson : null;
    }

    private bool HasPendingDelivery => EmailState == ChannelDeliveryState.Pending || SmsState == ChannelDeliveryState.Pending;

    private static ChannelDeliveryState SmsStateFor(NotificationChannels channels, bool smsEnabled)
    {
        if (!channels.HasFlag(NotificationChannels.Sms))
        {
            return ChannelDeliveryState.NotRequested;
        }

        return smsEnabled ? ChannelDeliveryState.Pending : ChannelDeliveryState.Disabled;
    }

    private void ApplyInApp(InAppTexts? texts)
    {
        if (texts is null)
        {
            return;
        }

        TitleAr = texts.TitleAr;
        TitleEn = texts.TitleEn;
        BodyAr = texts.BodyAr;
        BodyEn = texts.BodyEn;
    }
}

/// <summary>The values of a new user notification.</summary>
/// <param name="UserId">The recipient.</param>
/// <param name="TemplateKey">The template key.</param>
/// <param name="PlaceholdersJson">The placeholder values as JSON (kept only while a delivery waits).</param>
/// <param name="Channels">The channels asked for.</param>
/// <param name="Link">The bell link, or null.</param>
/// <param name="InApp">The rendered bell texts, or null without the in-app channel.</param>
/// <param name="SmsEnabled">Whether SMS is switched on.</param>
/// <param name="CreatedAt">Now.</param>
public sealed record UserNotificationDraft(
    long UserId,
    string TemplateKey,
    string PlaceholdersJson,
    NotificationChannels Channels,
    string? Link,
    InAppTexts? InApp,
    bool SmsEnabled,
    DateTimeOffset CreatedAt);

/// <summary>The values of a new addressed e-mail.</summary>
/// <param name="EmailAddress">The address.</param>
/// <param name="Language">The language to render.</param>
/// <param name="TemplateKey">The template key.</param>
/// <param name="PlaceholdersJson">The placeholder values as JSON.</param>
/// <param name="CreatedAt">Now.</param>
public sealed record AddressedDraft(string EmailAddress, AppLanguage Language, string TemplateKey, string PlaceholdersJson, DateTimeOffset CreatedAt);

/// <summary>The rendered bell texts in both languages.</summary>
/// <param name="TitleAr">The Arabic title.</param>
/// <param name="TitleEn">The English title.</param>
/// <param name="BodyAr">The Arabic body.</param>
/// <param name="BodyEn">The English body.</param>
public sealed record InAppTexts(string TitleAr, string TitleEn, string BodyAr, string BodyEn);
