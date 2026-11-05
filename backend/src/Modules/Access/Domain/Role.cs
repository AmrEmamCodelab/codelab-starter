namespace Starter.Modules.Access.Domain;

/// <summary>
/// A named set of permissions. An ordinary role is assigned to users (<see cref="UserRoleAssignment"/>, with a scope);
/// a virtual role (<see cref="VirtualKind"/> set) is seeded by a migration, never assigned, and reaches every active
/// user it matches. An inactive role grants nothing but keeps its permissions and assignments, so reactivating it
/// restores them. Roles are never deleted.
/// </summary>
public sealed class Role
{
    private readonly List<RolePermissionEntry> _permissions = [];

    private Role()
    {
    }

    /// <summary>The database id.</summary>
    public long Id { get; private set; }

    /// <summary>The stable code (<see cref="RoleCodeFormat"/>), unique; it never changes.</summary>
    public string Code { get; private set; } = string.Empty;

    /// <summary>The English name, unique.</summary>
    public string NameEn { get; private set; } = string.Empty;

    /// <summary>The Arabic name, unique.</summary>
    public string NameAr { get; private set; } = string.Empty;

    /// <summary>False when the role grants nothing.</summary>
    public bool IsActive { get; private set; }

    /// <summary>The users a virtual role reaches, or null for an ordinary (assignable) role.</summary>
    public VirtualRoleKind? VirtualKind { get; private set; }

    /// <summary>True for a virtual role (never assignable).</summary>
    public bool IsVirtual => VirtualKind is not null;

    /// <summary>When the role was created.</summary>
    public DateTimeOffset CreatedAt { get; private set; }

    /// <summary>When the role last changed.</summary>
    public DateTimeOffset UpdatedAt { get; private set; }

    /// <summary>The permissions the role grants.</summary>
    public IReadOnlyCollection<RolePermissionEntry> Permissions => _permissions;

    /// <summary>A new, active, ordinary role with no permission.</summary>
    /// <exception cref="ArgumentException">The code is malformed or a name is blank (the validator runs first).</exception>
    public static Role Create(string code, RoleNames names, DateTimeOffset createdAt)
    {
        ArgumentNullException.ThrowIfNull(names);
        if (!RoleCodeFormat.IsValid(code))
        {
            throw new ArgumentException("The role code is malformed.", nameof(code));
        }

        var role = new Role { Code = code, IsActive = true, CreatedAt = createdAt };
        role.Rename(names, createdAt);
        return role;
    }

    /// <summary>Changes the names (trimmed).</summary>
    public void Rename(RoleNames names, DateTimeOffset changedAt)
    {
        ArgumentNullException.ThrowIfNull(names);
        ArgumentException.ThrowIfNullOrWhiteSpace(names.En);
        ArgumentException.ThrowIfNullOrWhiteSpace(names.Ar);
        NameEn = names.En.Trim();
        NameAr = names.Ar.Trim();
        UpdatedAt = changedAt;
    }

    /// <summary>Activates or deactivates the role.</summary>
    public void SetActive(bool isActive, DateTimeOffset changedAt)
    {
        IsActive = isActive;
        UpdatedAt = changedAt;
    }

    /// <summary>
    /// Makes the role grant exactly <paramref name="permissions"/>: removes the others and adds the missing ones.
    /// Returns true when the set changed.
    /// </summary>
    public bool ReplacePermissions(IReadOnlyCollection<PermissionEntry> permissions, DateTimeOffset changedAt)
    {
        ArgumentNullException.ThrowIfNull(permissions);
        var wanted = permissions.Select(permission => permission.Key).ToHashSet(StringComparer.Ordinal);
        var removed = _permissions.RemoveAll(granted => !wanted.Contains(granted.Permission.Key));
        var held = _permissions.Select(granted => granted.Permission.Key).ToHashSet(StringComparer.Ordinal);
        var added = permissions.Where(permission => held.Add(permission.Key)).ToList();
        _permissions.AddRange(added.Select(permission => RolePermissionEntry.Create(this, permission)));
        var changed = removed > 0 || added.Count > 0;
        if (changed)
        {
            UpdatedAt = changedAt;
        }

        return changed;
    }
}

/// <summary>The bilingual names of a role.</summary>
/// <param name="En">The English name.</param>
/// <param name="Ar">The Arabic name.</param>
public sealed record RoleNames(string En, string Ar);
