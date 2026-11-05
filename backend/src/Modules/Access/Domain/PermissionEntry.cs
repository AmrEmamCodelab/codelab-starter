namespace Starter.Modules.Access.Domain;

/// <summary>
/// One permission of the catalog as stored in the database. The catalog is code, not data: rows are written only by
/// the permission synchronizer on start. A key that leaves the catalog is retired (<see cref="IsObsolete"/>), never
/// deleted, so existing grants keep their foreign key; a retired key grants nothing.
/// </summary>
public sealed class PermissionEntry
{
    private PermissionEntry(string key, string owner, string description)
    {
        Key = key;
        Owner = owner;
        Description = description;
    }

    /// <summary>The database id.</summary>
    public long Id { get; private set; }

    /// <summary>The permission key, unique (for example <c>dashboard.view</c>).</summary>
    public string Key { get; private set; }

    /// <summary>The owner segment of the key (the module in kebab case).</summary>
    public string Owner { get; private set; }

    /// <summary>What holding the permission allows, in English (for generated docs and admin screens).</summary>
    public string Description { get; private set; }

    /// <summary>True when the key is no longer in the code catalog.</summary>
    public bool IsObsolete { get; private set; }

    /// <summary>A new, current catalog row.</summary>
    public static PermissionEntry Create(string key, string owner, string description)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        ArgumentException.ThrowIfNullOrWhiteSpace(owner);
        ArgumentException.ThrowIfNullOrWhiteSpace(description);
        return new PermissionEntry(key: key, owner: owner, description: description);
    }

    /// <summary>
    /// Brings the code-owned fields up to date and un-retires the row. Returns true when anything changed, so an
    /// unchanged catalog writes nothing.
    /// </summary>
    public bool Refresh(string owner, string description)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(owner);
        ArgumentException.ThrowIfNullOrWhiteSpace(description);
        var changed = IsObsolete
            || !string.Equals(Owner, owner, StringComparison.Ordinal)
            || !string.Equals(Description, description, StringComparison.Ordinal);
        Owner = owner;
        Description = description;
        IsObsolete = false;
        return changed;
    }

    /// <summary>Marks the row retired. Returns true when it was current.</summary>
    public bool Retire()
    {
        if (IsObsolete)
        {
            return false;
        }

        IsObsolete = true;
        return true;
    }
}
