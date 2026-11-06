namespace Starter.Modules.Portal.Application;

/// <summary>
/// The permission keys the Portal module declares (format: <c>PermissionKeyFormat</c>). The public reads and the
/// contact form need none (anonymous, allowlisted).
/// </summary>
public static class PortalPermissionKeys
{
    /// <summary>Read the portal pages' content in the admin editor.</summary>
    public const string ContentView = "portal.content.view";

    /// <summary>Replace a portal page's content.</summary>
    public const string ContentManage = "portal.content.manage";

    // scaffold:permission-keys
}
