namespace Starter.Application.Lookups;

/// <summary>One active value of a lookup type, as another module reads it.</summary>
/// <param name="Id">The value id (a <c>&lt;Name&gt;Ids</c> constant of <c>Starter.Domain.Lookups</c>).</param>
/// <param name="Code">The kebab-case code.</param>
/// <param name="NameAr">The Arabic name.</param>
/// <param name="NameEn">The English name.</param>
public sealed record ActiveLookupValue(int Id, string Code, string NameAr, string NameEn);

/// <summary>
/// Reads lookup values for other modules (implemented by the Lookups module), so no module reads the Lookups tables or
/// references its project. Served from the Lookups per-node cache of each type's active list: a value retired through
/// the Lookups admin API is dropped at once on that node and within the Lookups cache TTL on the others.
/// </summary>
public interface ILookupValues
{
    /// <summary>
    /// The active value <paramref name="id"/> of the type <paramref name="typeKey"/> (a
    /// <c>Starter.Domain.Lookups.LookupTypes</c> constant), or null when the type or the value is unknown or the value
    /// is retired.
    /// </summary>
    Task<ActiveLookupValue?> FindActiveAsync(string typeKey, int id, CancellationToken cancellationToken);
}
