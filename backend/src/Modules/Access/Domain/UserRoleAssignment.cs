using Starter.Application.Authorization;

namespace Starter.Modules.Access.Domain;

/// <summary>
/// An ordinary role assigned to one user, with the scope its permissions reach. The user is referenced by its internal
/// id only (users live in another module; there is no cross-module foreign key, ADR-004). A virtual role is never
/// assigned.
/// </summary>
public sealed class UserRoleAssignment
{
    private UserRoleAssignment()
    {
    }

    /// <summary>The database id.</summary>
    public long Id { get; private set; }

    /// <summary>The internal id of the user who holds the role.</summary>
    public long UserId { get; private set; }

    /// <summary>The assigned role's id.</summary>
    public long RoleId { get; private set; }

    /// <summary>The assigned role.</summary>
    public Role Role { get; private set; } = null!;

    /// <summary>How far the role's permissions reach for this user.</summary>
    public PermissionScope Scope { get; private set; }

    /// <summary>When the role was assigned (or its scope last changed).</summary>
    public DateTimeOffset AssignedAt { get; private set; }

    /// <summary>A new assignment of <paramref name="role"/> to <paramref name="userId"/>.</summary>
    /// <exception cref="ArgumentException">The role is virtual (the use case refuses that first).</exception>
    public static UserRoleAssignment Create(long userId, Role role, PermissionScope scope, DateTimeOffset assignedAt)
    {
        ArgumentNullException.ThrowIfNull(role);
        if (role.IsVirtual)
        {
            throw new ArgumentException("A virtual role is never assigned.", nameof(role));
        }

        return new UserRoleAssignment { UserId = userId, Role = role, RoleId = role.Id, Scope = scope, AssignedAt = assignedAt };
    }

    /// <summary>Changes the scope; returns true when it changed.</summary>
    public bool ChangeScope(PermissionScope scope, DateTimeOffset changedAt)
    {
        if (Scope == scope)
        {
            return false;
        }

        Scope = scope;
        AssignedAt = changedAt;
        return true;
    }
}
