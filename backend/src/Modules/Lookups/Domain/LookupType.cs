namespace Starter.Modules.Lookups.Domain;

/// <summary>
/// One lookup type (<c>lookups.LookupTypes</c>): its kebab-case <see cref="Key"/> (a
/// <c>Starter.Domain.Lookups.LookupTypes</c> constant) and its Arabic and English names for the admin screen. Created
/// only by a migration; never changed at run time.
/// </summary>
public sealed class LookupType
{
    private LookupType()
    {
    }

    /// <summary>The type key, for example <c>contact-topics</c>; the primary key.</summary>
    public string Key { get; private set; } = string.Empty;

    /// <summary>The Arabic name of the type.</summary>
    public string NameAr { get; private set; } = string.Empty;

    /// <summary>The English name of the type.</summary>
    public string NameEn { get; private set; } = string.Empty;

    /// <summary>A type as a migration seeds it (tests build the same rows).</summary>
    public static LookupType Seeded(string key, LookupNames names)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        ArgumentNullException.ThrowIfNull(names);
        return new LookupType { Key = key, NameAr = names.Ar, NameEn = names.En };
    }
}

/// <summary>An Arabic and an English name (one pair, so the two can never be swapped at a call site).</summary>
/// <param name="Ar">The Arabic name.</param>
/// <param name="En">The English name.</param>
public sealed record LookupNames(string Ar, string En);
