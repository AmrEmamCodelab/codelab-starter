namespace Starter.Modules.Notifications.Application;

/// <summary>
/// The Notifications module's error codes. The client renders <c>errors.&lt;CODE&gt;</c> from its ar and en bundles.
/// </summary>
public static class NotificationsErrorCodes
{
    /// <summary>The notification does not exist or is not the caller's (404).</summary>
    public const string NotificationNotFound = "NOTIFICATIONS_NOTIFICATION_NOT_FOUND";

    /// <summary>The caller has no user account to notify (404).</summary>
    public const string RecipientNotFound = "NOTIFICATIONS_RECIPIENT_NOT_FOUND";

    /// <summary>The recipient has no e-mail address for an immediate e-mail (a caller defect, 422).</summary>
    public const string RecipientHasNoEmail = "NOTIFICATIONS_RECIPIENT_HAS_NO_EMAIL";

    /// <summary>No template text exists for the key, channel and language (a seeding defect, 422).</summary>
    public const string TemplateNotFound = "NOTIFICATIONS_TEMPLATE_NOT_FOUND";

    /// <summary>The template uses a placeholder the caller gave no value for (a caller defect, 422).</summary>
    public const string PlaceholderMissing = "NOTIFICATIONS_PLACEHOLDER_MISSING";

    /// <summary>The notification itself is malformed: no channel, unknown channel, blank key or a too-long link (422).</summary>
    public const string InvalidNotification = "NOTIFICATIONS_INVALID_NOTIFICATION";

    // scaffold:error-codes
}
