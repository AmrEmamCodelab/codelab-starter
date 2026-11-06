using Starter.Application.Persistence;
using Starter.Modules.Lookups.Domain;

namespace Starter.Modules.Lookups.Application;

/// <summary>The Lookups module's persistence boundary (implemented by its EF Core context in Infrastructure).</summary>
public interface ILookupsUnitOfWork : IUnitOfWork;

/// <summary>The lookup types and their values (read, plus the tracked value an activation change edits).</summary>
public interface ILookupRepository
{
    /// <summary>Every lookup type with the number of its values (active or not), ordered by key.</summary>
    Task<IReadOnlyList<LookupTypeSummary>> ListTypesAsync(CancellationToken cancellationToken);

    /// <summary>True when a lookup type has the key <paramref name="typeKey"/>.</summary>
    Task<bool> TypeExistsAsync(string typeKey, CancellationToken cancellationToken);

    /// <summary>
    /// The values of <paramref name="typeKey"/> (only the active ones when <paramref name="activeOnly"/>), ordered by
    /// sort order, then id; empty for an unknown type. Read-only (not tracked).
    /// </summary>
    Task<IReadOnlyList<LookupItem>> ListItemsAsync(string typeKey, bool activeOnly, CancellationToken cancellationToken);

    /// <summary>The value, tracked for a change written by the unit of work, or null.</summary>
    Task<LookupItem?> FindItemAsync(LookupItemKey key, CancellationToken cancellationToken);
}

/// <summary>
/// The per-node cache of each type's active list. An entry lives at most <see cref="LookupsLimits.CacheTtl"/>, timed by
/// the injected <see cref="TimeProvider"/>.
/// </summary>
public interface ILookupCache
{
    /// <summary>The cached active list of <paramref name="typeKey"/>, or null when there is none or it expired.</summary>
    IReadOnlyList<LookupValue>? Find(string typeKey);

    /// <summary>Caches the active list of <paramref name="typeKey"/> for <see cref="LookupsLimits.CacheTtl"/>.</summary>
    void Store(string typeKey, IReadOnlyList<LookupValue> values);

    /// <summary>Drops the cached list of <paramref name="typeKey"/> (after a value of it changed).</summary>
    void Invalidate(string typeKey);
}

/// <summary>The address of one lookup value: its type and its id within the type.</summary>
/// <param name="TypeKey">The type key (<c>Starter.Domain.Lookups.LookupTypes</c>).</param>
/// <param name="Id">The value id (a <c>&lt;Name&gt;Ids</c> constant).</param>
public sealed record LookupItemKey(string TypeKey, int Id);

/// <summary>One lookup type with its value count.</summary>
/// <param name="Key">The type key.</param>
/// <param name="NameAr">The Arabic name.</param>
/// <param name="NameEn">The English name.</param>
/// <param name="ItemCount">The number of its values, active or not.</param>
public sealed record LookupTypeSummary(string Key, string NameAr, string NameEn, int ItemCount);

/// <summary>One active lookup value as the read API serves it.</summary>
/// <param name="Id">The value id.</param>
/// <param name="Code">The kebab-case code.</param>
/// <param name="NameAr">The Arabic name.</param>
/// <param name="NameEn">The English name.</param>
/// <param name="SortOrder">The display order.</param>
public sealed record LookupValue(int Id, string Code, string NameAr, string NameEn, int SortOrder)
{
    /// <summary>The read view of <paramref name="item"/>.</summary>
    public static LookupValue From(LookupItem item)
    {
        ArgumentNullException.ThrowIfNull(item);
        return new LookupValue(item.Id, item.Code, item.NameAr, item.NameEn, item.SortOrder);
    }
}
