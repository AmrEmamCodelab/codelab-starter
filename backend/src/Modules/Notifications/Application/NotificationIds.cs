using Codelab.Security.PublicIds;

namespace Starter.Modules.Notifications.Application;

/// <summary>The family of notification ids on the wire (<c>PublicId&lt;NotificationIds&gt;</c>).</summary>
public sealed class NotificationIds : IIdDomain
{
    /// <summary>The domain label bound into every notification PublicId (part of the wire format; never rename).</summary>
    public const string DomainLabel = "notifications";

    /// <inheritdoc />
    public static string Domain => DomainLabel;
}
