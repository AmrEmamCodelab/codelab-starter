namespace Starter.Modules.Notifications.Application;

/// <summary>
/// The permission keys the Notifications module declares (format: <c>PermissionKeyFormat</c>). Reading one's own feed
/// needs none (any signed-in user, allowlisted).
/// </summary>
public static class NotificationsPermissionKeys
{
    /// <summary>Send oneself a test notification (in-app, e-mail and, when enabled, SMS).</summary>
    public const string Test = "notifications.test";

    // scaffold:permission-keys
}
