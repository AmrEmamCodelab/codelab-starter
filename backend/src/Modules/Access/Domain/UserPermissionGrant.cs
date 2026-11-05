namespace Starter.Modules.Access.Domain;

/// <summary>
/// A permission granted directly to one user. The user is referenced by its internal id only (the Identity module
/// owns users; there is no cross-module foreign key, ADR-004).
/// </summary>
public sealed class UserPermissionGrant
{
    private UserPermissionGrant()
    {
    }

    /// <summary>The database id.</summary>
    public long Id { get; private set; }

    /// <summary>The internal id of the user who holds the grant.</summary>
    public long UserId { get; private set; }

    /// <summary>The granted permission's id.</summary>
    public long PermissionId { get; private set; }

    /// <summary>The granted permission.</summary>
    public PermissionEntry Permission { get; private set; } = null!;

    /// <summary>When the grant was made.</summary>
    public DateTimeOffset GrantedAt { get; private set; }

    /// <summary>A new grant of <paramref name="permission"/> to <paramref name="userId"/>.</summary>
    public static UserPermissionGrant Create(long userId, PermissionEntry permission, DateTimeOffset grantedAt)
    {
        ArgumentNullException.ThrowIfNull(permission);
        return new UserPermissionGrant
        {
            UserId = userId,
            Permission = permission,
            PermissionId = permission.Id,
            GrantedAt = grantedAt,
        };
    }
}
