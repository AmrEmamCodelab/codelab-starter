using Microsoft.EntityFrameworkCore;
using Starter.Modules.Lookups.Application;
using Starter.Modules.Lookups.Domain;

namespace Starter.Modules.Lookups.Infrastructure.Persistence;

/// <summary>The EF Core <see cref="ILookupRepository"/> over <see cref="LookupsDbContext"/>.</summary>
public sealed class LookupRepository(LookupsDbContext context) : ILookupRepository
{
    /// <inheritdoc />
    public async Task<IReadOnlyList<LookupTypeSummary>> ListTypesAsync(CancellationToken cancellationToken) =>
        await context.LookupTypes
            .AsNoTracking()
            .OrderBy(type => type.Key)
            .Select(type => new LookupTypeSummary(
                type.Key,
                type.NameAr,
                type.NameEn,
                context.LookupItems.Count(item => item.TypeKey == type.Key)))
            .ToListAsync(cancellationToken);

    /// <inheritdoc />
    public Task<bool> TypeExistsAsync(string typeKey, CancellationToken cancellationToken) =>
        context.LookupTypes.AnyAsync(type => type.Key == typeKey, cancellationToken);

    /// <inheritdoc />
    public async Task<IReadOnlyList<LookupItem>> ListItemsAsync(string typeKey, bool activeOnly, CancellationToken cancellationToken) =>
        await context.LookupItems
            .AsNoTracking()
            .Where(item => item.TypeKey == typeKey && (!activeOnly || item.IsActive))
            .OrderBy(item => item.SortOrder)
            .ThenBy(item => item.Id)
            .ToListAsync(cancellationToken);

    /// <inheritdoc />
    public Task<LookupItem?> FindItemAsync(LookupItemKey key, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(key);
        return context.LookupItems.SingleOrDefaultAsync(item => item.TypeKey == key.TypeKey && item.Id == key.Id, cancellationToken);
    }
}
