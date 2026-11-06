namespace Starter.Domain.Lookups;

/// <summary>
/// The shape every lookup (reference-data) value shares: a fixed integer <see cref="Id"/> that the seeding migration
/// writes and code compares only through the type's <c>&lt;Name&gt;Ids</c> constants class (for example
/// <see cref="ContactTopicIds"/>), a stable machine <see cref="Code"/>, the Arabic and English names, the display order
/// and an active flag. Values are seeded only by migrations; at run time only <see cref="IsActive"/> changes.
/// </summary>
public abstract class LookupEntity
{
    /// <summary>The fixed id, unique within the lookup type (never generated; a <c>&lt;Name&gt;Ids</c> constant).</summary>
    public int Id { get; protected set; }

    /// <summary>The stable machine code (kebab-case, unique within the type); never shown to users.</summary>
    public string Code { get; protected set; } = string.Empty;

    /// <summary>The Arabic display name.</summary>
    public string NameAr { get; protected set; } = string.Empty;

    /// <summary>The English display name.</summary>
    public string NameEn { get; protected set; } = string.Empty;

    /// <summary>The display order within the type (ascending; ties fall back to <see cref="Id"/>).</summary>
    public int SortOrder { get; protected set; }

    /// <summary>
    /// False when the value is retired: it is no longer offered for new data, while stored references to it stay
    /// valid and still resolve.
    /// </summary>
    public bool IsActive { get; protected set; } = true;

    /// <summary>Switches the value on or off; true when the state changed (repeating the current state is a no-op).</summary>
    public bool SetActive(bool isActive)
    {
        if (IsActive == isActive)
        {
            return false;
        }

        IsActive = isActive;
        return true;
    }
}
