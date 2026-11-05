using Starter.Application.Authorization;

namespace Starter.Modules.Access.Domain;

/// <summary>
/// A permission granted directly to one user, with the scope it reaches. The user is referenced by its internal id
/// only (users live in another module; there is no cross-module foreign key, ADR-004).
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

    /// <summary>How far the grant reaches (<see cref="PermissionScope.Global"/> for grants made before scopes existed).</summary>
    public PermissionScope Scope { get; private set; }

    /// <summary>When the grant was made (or its scope last changed).</summary>
    public DateTimeOffset GrantedAt { get; private set; }

    /// <summary>A new grant of <paramref name="permission"/> to <paramref name="userId"/>, global unless a scope is given.</summary>
    public static UserPermissionGrant Create(
        long userId,
        PermissionEntry permission,
        DateTimeOffset grantedAt,
        PermissionScope scope = PermissionScope.Global)
    {
        ArgumentNullException.ThrowIfNull(permission);
        return new UserPermissionGrant
        {
            UserId = userId,
            Permission = permission,
            PermissionId = permission.Id,
            Scope = scope,
            GrantedAt = grantedAt,
        };
    }

    /// <summary>Changes the scope; returns true when it changed.</summary>
    public bool ChangeScope(PermissionScope scope, DateTimeOffset changedAt)
    {
        if (Scope == scope)
        {
            return false;
        }

        Scope = scope;
        GrantedAt = changedAt;
        return true;
    }
}
