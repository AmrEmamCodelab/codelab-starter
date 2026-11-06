using Starter.Domain.Lookups;

namespace Starter.Modules.Lookups.Application;

/// <summary>The shape of a lookup type key taken from a route (one owner for every use case that reads one).</summary>
public static class LookupTypeKeys
{
    /// <summary>
    /// True when <paramref name="typeKey"/> can name a lookup type: kebab-case ASCII within
    /// <see cref="LookupLimits.TypeKeyMaxLength"/>. Anything else is answered as an unknown type without a query.
    /// </summary>
    public static bool IsWellFormed(string? typeKey) => LookupLimits.IsKebab(typeKey, LookupLimits.TypeKeyMaxLength);
}
