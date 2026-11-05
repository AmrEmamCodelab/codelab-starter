namespace Starter.Modules.Access.Domain;

/// <summary>One permission a role grants. Created and removed only through <see cref="Role.ReplacePermissions"/>.</summary>
public sealed class RolePermissionEntry
{
    private RolePermissionEntry()
    {
    }

    /// <summary>The database id.</summary>
    public long Id { get; private set; }

    /// <summary>The role's id.</summary>
    public long RoleId { get; private set; }

    /// <summary>The role.</summary>
    public Role Role { get; private set; } = null!;

    /// <summary>The granted permission's id.</summary>
    public long PermissionId { get; private set; }

    /// <summary>The granted permission.</summary>
    public PermissionEntry Permission { get; private set; } = null!;

    internal static RolePermissionEntry Create(Role role, PermissionEntry permission) => new()
    {
        Role = role,
        RoleId = role.Id,
        Permission = permission,
        PermissionId = permission.Id,
    };
}
