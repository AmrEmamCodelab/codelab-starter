namespace Starter.Application.Settings;

/// <summary>
/// The keys of the runtime settings the platform seeds (the Settings module's migrations insert them). A module that
/// owns its own settings declares a <c>&lt;Module&gt;SettingKeys</c> class the same way and inserts its rows with a
/// Settings migration. Format: <c>&lt;group&gt;.&lt;kebab-name&gt;</c>, lower-case; the group is the part before the first dot.
/// </summary>
public static class SettingKeys
{
    /// <summary>Minutes of inactivity after which the web app shows the idle warning (integer, 1-240, default 15).</summary>
    public const string SessionIdleWarningMinutes = "session.idle-warning-minutes";

    /// <summary>
    /// How many notifications one feed page shows when the caller names no page size (integer, 5-100, default 20; the
    /// feed caps any page at 50). Read by the Notifications feed.
    /// </summary>
    public const string NotificationsPageSize = "notifications.page-size";

    /// <summary>Whether the public contact form accepts messages (boolean, default true). Read by the Portal contact use case.</summary>
    public const string PortalContactEnabled = "portal.contact-enabled";
}
