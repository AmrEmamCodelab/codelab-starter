using Starter.Domain.Lookups;

namespace Starter.Modules.Lookups.Domain;

/// <summary>
/// One value of a lookup type (<c>lookups.LookupItems</c>, key <see cref="TypeKey"/> + <see cref="LookupEntity.Id"/>).
/// Seeded only by a migration; at run time only its active flag changes (<see cref="LookupEntity.SetActive"/>).
/// </summary>
public sealed class LookupItem : LookupEntity
{
    private LookupItem()
    {
    }

    /// <summary>The key of the type this value belongs to (<c>Starter.Domain.Lookups.LookupTypes</c>).</summary>
    public string TypeKey { get; private set; } = string.Empty;

    /// <summary>A value as a migration seeds it (tests build the same rows).</summary>
    public static LookupItem Seeded(string typeKey, LookupItemSeed seed)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(typeKey);
        ArgumentNullException.ThrowIfNull(seed);
        return new LookupItem
        {
            TypeKey = typeKey,
            Id = seed.Id,
            Code = seed.Code,
            NameAr = seed.Names.Ar,
            NameEn = seed.Names.En,
            SortOrder = seed.SortOrder,
            IsActive = seed.IsActive,
        };
    }
}

/// <summary>The seeded content of one lookup value.</summary>
/// <param name="Id">The fixed id (a <c>&lt;Name&gt;Ids</c> constant).</param>
/// <param name="Code">The kebab-case code, unique within the type.</param>
/// <param name="Names">The Arabic and English names.</param>
/// <param name="SortOrder">The display order within the type.</param>
/// <param name="IsActive">Whether the value is offered (seeded values are active).</param>
public sealed record LookupItemSeed(int Id, string Code, LookupNames Names, int SortOrder, bool IsActive = true);
