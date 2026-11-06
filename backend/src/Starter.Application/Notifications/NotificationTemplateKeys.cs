namespace Starter.Application.Notifications;

/// <summary>
/// The notification templates the Notifications module seeds (by migration, Arabic and English per channel). A new
/// template is a new constant here (or in the owning module) plus a Notifications migration that inserts its rows.
/// </summary>
public static class NotificationTemplateKeys
{
    /// <summary>
    /// The account activation e-mail. Placeholders: <see cref="NotificationPlaceholders.ActivationLink"/>,
    /// <see cref="NotificationPlaceholders.ExpiresAt"/>. Channel: e-mail (send it with
    /// <see cref="INotificationService.SendImmediateEmailAsync"/>: the link carries a sealed ticket).
    /// </summary>
    public const string AccountActivation = "account-activation";

    /// <summary>
    /// A contact-us message arrived (to the contact inbox). Placeholders:
    /// <see cref="NotificationPlaceholders.ReferenceNumber"/>, <see cref="NotificationPlaceholders.FullName"/>,
    /// <see cref="NotificationPlaceholders.Email"/>, <see cref="NotificationPlaceholders.Topic"/>,
    /// <see cref="NotificationPlaceholders.Message"/>. Channel: e-mail.
    /// </summary>
    public const string ContactReceived = "contact-received";

    /// <summary>
    /// The administrator's test notification. Placeholder: <see cref="NotificationPlaceholders.SentAt"/>. Channels:
    /// in-app, e-mail and SMS.
    /// </summary>
    public const string TestNotification = "test-notification";
}

/// <summary>The placeholder names the seeded templates use (<c>{{name}}</c> in the template text; case-sensitive).</summary>
public static class NotificationPlaceholders
{
    /// <summary>The activation link (account-activation).</summary>
    public const string ActivationLink = "activationLink";

    /// <summary>When the activation link stops working, as display text (account-activation).</summary>
    public const string ExpiresAt = "expiresAt";

    /// <summary>The contact message's reference number, for example <c>CU-2026-001</c> (contact-received).</summary>
    public const string ReferenceNumber = "referenceNumber";

    /// <summary>The sender's full name (contact-received).</summary>
    public const string FullName = "fullName";

    /// <summary>The sender's e-mail address (contact-received).</summary>
    public const string Email = "email";

    /// <summary>The contact topic's name in the template's language (contact-received).</summary>
    public const string Topic = "topic";

    /// <summary>The message text (contact-received).</summary>
    public const string Message = "message";

    /// <summary>When the test was sent, as display text (test-notification).</summary>
    public const string SentAt = "sentAt";
}
