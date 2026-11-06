using Starter.Application.Authorization;

namespace Starter.Modules.Notifications.Application;

/// <summary>The Notifications module's share of the permission catalog.</summary>
public sealed class NotificationsPermissionCatalogSource : IPermissionCatalogSource
{
    /// <inheritdoc />
    public IReadOnlyCollection<PermissionDefinition> Permissions { get; } =
    [
        new(Key: NotificationsPermissionKeys.Test, Description: "Send oneself a test notification to check the bell, e-mail and SMS delivery."),
        // scaffold:permission-catalog
    ];
}
