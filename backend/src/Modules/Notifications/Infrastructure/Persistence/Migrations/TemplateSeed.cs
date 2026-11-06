using Starter.Application.Notifications;
using Starter.Domain.Localization;
using Starter.Modules.Notifications.Domain;

namespace Starter.Modules.Notifications.Infrastructure.Persistence.Migrations;

/// <summary>
/// The template texts <see cref="InitialCreate"/> inserts. Frozen with the migration: changed wording or a new
/// template is a NEW migration that updates or inserts rows, never an edit here. The module tests check that every
/// text renders with the placeholders its key documents (<see cref="NotificationPlaceholders"/>) and that each key has
/// Arabic and English for each of its channels. E-mail bodies are HTML (the Arabic ones right to left).
/// </summary>
public static class TemplateSeed
{
    /// <summary>The rows, by key, channel and language.</summary>
    public static IReadOnlyList<TemplateText> Rows { get; } =
    [
        .. AccountActivation(),
        .. ContactReceived(),
        .. TestNotification(),
    ];

    private static TemplateText[] AccountActivation() =>
    [
        Email(
            NotificationTemplateKeys.AccountActivation,
            AppLanguage.Arabic,
            "تفعيل حسابك",
            "<div dir=\"rtl\"><p>مرحباً،</p><p>تم إنشاء حساب لك. افتح الرابط أدناه لتعيين كلمة المرور وتفعيل الحساب.</p>"
            + "<p><a href=\"{{activationLink}}\">تفعيل حسابي</a></p>"
            + "<p>يعمل الرابط مرة واحدة وتنتهي صلاحيته في {{expiresAt}}. إذا لم تكن تتوقع هذه الرسالة فتجاهلها.</p></div>"),
        Email(
            NotificationTemplateKeys.AccountActivation,
            AppLanguage.English,
            "Activate your account",
            "<p>Hello,</p><p>An account has been created for you. Open the link below to set your password and activate it.</p>"
            + "<p><a href=\"{{activationLink}}\">Activate my account</a></p>"
            + "<p>The link works once and expires on {{expiresAt}}. If you did not expect this message, ignore it.</p>"),
    ];

    private static TemplateText[] ContactReceived() =>
    [
        Email(
            NotificationTemplateKeys.ContactReceived,
            AppLanguage.Arabic,
            "رسالة تواصل جديدة {{referenceNumber}}",
            "<div dir=\"rtl\"><p>وصلت رسالة جديدة عبر نموذج التواصل في البوابة.</p><ul>"
            + "<li>الرقم المرجعي: {{referenceNumber}}</li><li>الاسم: {{fullName}}</li>"
            + "<li>البريد الإلكتروني: {{email}}</li><li>الموضوع: {{topic}}</li></ul>"
            + "<p style=\"white-space:pre-line\">{{message}}</p></div>"),
        Email(
            NotificationTemplateKeys.ContactReceived,
            AppLanguage.English,
            "New contact message {{referenceNumber}}",
            "<p>A new message arrived through the portal contact form.</p><ul>"
            + "<li>Reference number: {{referenceNumber}}</li><li>Name: {{fullName}}</li>"
            + "<li>E-mail: {{email}}</li><li>Topic: {{topic}}</li></ul>"
            + "<p style=\"white-space:pre-line\">{{message}}</p>"),
    ];

    private static TemplateText[] TestNotification() =>
    [
        new(NotificationTemplateKeys.TestNotification, NotificationChannels.InApp, AppLanguage.Arabic, "إشعار تجريبي", "هذا إشعار تجريبي أُرسل في {{sentAt}}. الإشعارات داخل النظام تعمل."),
        new(NotificationTemplateKeys.TestNotification, NotificationChannels.InApp, AppLanguage.English, "Test notification", "This is a test notification sent at {{sentAt}}. In-app notifications work."),
        Email(
            NotificationTemplateKeys.TestNotification,
            AppLanguage.Arabic,
            "إشعار تجريبي",
            "<div dir=\"rtl\"><p>هذه رسالة بريد إلكتروني تجريبية أُرسلت في {{sentAt}}. إرسال البريد الإلكتروني يعمل.</p></div>"),
        Email(
            NotificationTemplateKeys.TestNotification,
            AppLanguage.English,
            "Test notification",
            "<p>This is a test e-mail sent at {{sentAt}}. E-mail delivery works.</p>"),
        new(NotificationTemplateKeys.TestNotification, NotificationChannels.Sms, AppLanguage.Arabic, Subject: null, "رسالة نصية تجريبية أُرسلت في {{sentAt}}. إرسال الرسائل النصية يعمل."),
        new(NotificationTemplateKeys.TestNotification, NotificationChannels.Sms, AppLanguage.English, Subject: null, "Test SMS sent at {{sentAt}}. SMS delivery works."),
    ];

    private static TemplateText Email(string key, AppLanguage language, string subject, string body) =>
        new(key, NotificationChannels.Email, language, subject, body);
}
