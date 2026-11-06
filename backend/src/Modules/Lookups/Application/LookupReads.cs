using Starter.Application.Results;
using Starter.Modules.Lookups.Domain;

namespace Starter.Modules.Lookups.Application;

/// <summary>
/// <c>GET /api/lookups/{type}</c>: the active values of one type, for any signed-in user, served from the per-node
/// cache (<see cref="ILookupCache"/>) and read from the database on a miss.
/// </summary>
public sealed class GetActiveLookupValues(ILookupRepository lookups, ILookupCache cache)
{
    /// <summary>The active values ordered by sort order, or <see cref="LookupsErrorCodes.TypeNotFound"/> (404).</summary>
    public async Task<Outcome<IReadOnlyList<LookupValue>>> ExecuteAsync(string? typeKey, CancellationToken cancellationToken)
    {
        if (!LookupTypeKeys.IsWellFormed(typeKey))
        {
            return Refusal.NotFound(LookupsErrorCodes.TypeNotFound);
        }

        if (cache.Find(typeKey!) is { } cached)
        {
            return Outcome.Success(cached);
        }

        if (!await lookups.TypeExistsAsync(typeKey!, cancellationToken))
        {
            return Refusal.NotFound(LookupsErrorCodes.TypeNotFound);
        }

        var items = await lookups.ListItemsAsync(typeKey!, activeOnly: true, cancellationToken);
        IReadOnlyList<LookupValue> values = [.. items.Select(LookupValue.From)];
        cache.Store(typeKey!, values);
        return Outcome.Success(values);
    }
}

/// <summary><c>GET /api/admin/lookups</c>: every lookup type with its value count (never cached: the admin sees the database).</summary>
public sealed class ListLookupTypes(ILookupRepository lookups)
{
    /// <summary>The types ordered by key (never refused).</summary>
    public async Task<Outcome<IReadOnlyList<LookupTypeSummary>>> ExecuteAsync(CancellationToken cancellationToken) =>
        Outcome.Success(await lookups.ListTypesAsync(cancellationToken));
}

/// <summary><c>GET /api/admin/lookups/{type}</c>: every value of one type, active or not (never cached).</summary>
public sealed class ListLookupItems(ILookupRepository lookups)
{
    /// <summary>The values ordered by sort order, or <see cref="LookupsErrorCodes.TypeNotFound"/> (404).</summary>
    public async Task<Outcome<IReadOnlyList<LookupItem>>> ExecuteAsync(string? typeKey, CancellationToken cancellationToken)
    {
        if (!LookupTypeKeys.IsWellFormed(typeKey) || !await lookups.TypeExistsAsync(typeKey!, cancellationToken))
        {
            return Refusal.NotFound(LookupsErrorCodes.TypeNotFound);
        }

        return Outcome.Success(await lookups.ListItemsAsync(typeKey!, activeOnly: false, cancellationToken));
    }
}
