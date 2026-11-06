using Starter.Domain.Lookups;
using Starter.Modules.Lookups.Application;
using Starter.Modules.Lookups.Domain;

namespace Starter.Modules.Lookups.Tests.Fakes;

/// <summary>An in-memory lookup store: types and their values, ordered like the EF repository.</summary>
internal sealed class FakeLookupRepository : ILookupRepository
{
    public const string OtherType = "other-type";

    public List<LookupType> Types { get; } = [];

    public List<LookupItem> Items { get; } = [];

    public int ItemReads { get; private set; }

    public int TypeChecks { get; private set; }

    /// <summary>The contact topics as their migration seeds them, plus one more type with one inactive value.</summary>
    public static FakeLookupRepository Seeded()
    {
        var repository = new FakeLookupRepository();
        repository.Types.Add(LookupType.Seeded(LookupTypes.ContactTopics, new LookupNames(Ar: "مواضيع التواصل", En: "Contact topics")));
        repository.Types.Add(LookupType.Seeded(OtherType, new LookupNames(Ar: "نوع آخر", En: "Other type")));
        repository.Items.Add(Item(LookupTypes.ContactTopics, ContactTopicIds.Suggestion, "suggestion", sortOrder: 40));
        repository.Items.Add(Item(LookupTypes.ContactTopics, ContactTopicIds.General, "general", sortOrder: 10));
        repository.Items.Add(Item(LookupTypes.ContactTopics, ContactTopicIds.Account, "account", sortOrder: 30));
        repository.Items.Add(Item(LookupTypes.ContactTopics, ContactTopicIds.Request, "request", sortOrder: 20));
        repository.Items.Add(Item(OtherType, id: 1, "only", sortOrder: 10, isActive: false));
        return repository;
    }

    public static LookupItem Item(string typeKey, int id, string code, int sortOrder, bool isActive = true) =>
        LookupItem.Seeded(typeKey, new LookupItemSeed(Id: id, Code: code, Names: new LookupNames(Ar: code + "-ar", En: code + "-en"), SortOrder: sortOrder, IsActive: isActive));

    public Task<IReadOnlyList<LookupTypeSummary>> ListTypesAsync(CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<LookupTypeSummary>>([.. Types
            .OrderBy(type => type.Key, StringComparer.Ordinal)
            .Select(type => new LookupTypeSummary(type.Key, type.NameAr, type.NameEn, Items.Count(item => item.TypeKey == type.Key)))]);

    public Task<bool> TypeExistsAsync(string typeKey, CancellationToken cancellationToken)
    {
        TypeChecks++;
        return Task.FromResult(Types.Any(type => type.Key == typeKey));
    }

    public Task<IReadOnlyList<LookupItem>> ListItemsAsync(string typeKey, bool activeOnly, CancellationToken cancellationToken)
    {
        ItemReads++;
        return Task.FromResult<IReadOnlyList<LookupItem>>([.. Items
            .Where(item => item.TypeKey == typeKey && (!activeOnly || item.IsActive))
            .OrderBy(item => item.SortOrder)
            .ThenBy(item => item.Id)]);
    }

    public Task<LookupItem?> FindItemAsync(LookupItemKey key, CancellationToken cancellationToken) =>
        Task.FromResult(Items.SingleOrDefault(item => item.TypeKey == key.TypeKey && item.Id == key.Id));
}

/// <summary>Counts the saves.</summary>
internal sealed class FakeUnitOfWork : ILookupsUnitOfWork
{
    public int Saves { get; private set; }

    public Task<int> SaveChangesAsync(CancellationToken cancellationToken)
    {
        Saves++;
        return Task.FromResult(1);
    }
}

/// <summary>A dictionary cache that records invalidations (the TTL is the real cache's job, tested on its own).</summary>
internal sealed class RecordingLookupCache : ILookupCache
{
    private readonly Dictionary<string, IReadOnlyList<LookupValue>> _entries = new(StringComparer.Ordinal);

    public List<string> Invalidated { get; } = [];

    public IReadOnlyList<LookupValue>? Find(string typeKey) => _entries.GetValueOrDefault(typeKey);

    public void Store(string typeKey, IReadOnlyList<LookupValue> values) => _entries[typeKey] = values;

    public void Invalidate(string typeKey)
    {
        Invalidated.Add(typeKey);
        _entries.Remove(typeKey);
    }
}
