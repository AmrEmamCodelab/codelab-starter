using System.Collections.Concurrent;

namespace Package.Sample.Api.Records;

/// <summary>One stored record.</summary>
public sealed record SampleRecord(long Id, string Title, bool Archived);

/// <summary>An in-memory record store (the sample has no database). Ids are internal; the wire sees PublicIds.</summary>
public sealed class RecordStore
{
    /// <summary>The id of the record every store starts with.</summary>
    public const long SeedRecordId = 42;

    /// <summary>The title of the seed record.</summary>
    public const string SeedTitle = "Seed record";

    private readonly ConcurrentDictionary<long, SampleRecord> _records = new();
    private long _lastId = SeedRecordId;

    /// <summary>Creates a store holding the seed record.</summary>
    public RecordStore() => _records[SeedRecordId] = new SampleRecord(SeedRecordId, SeedTitle, Archived: false);

    /// <summary>The record with <paramref name="id"/>, or null.</summary>
    public SampleRecord? Find(long id) => _records.TryGetValue(id, out var record) ? record : null;

    /// <summary>Stores a new record under the next id.</summary>
    public SampleRecord Add(string title)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(title);
        var record = new SampleRecord(Interlocked.Increment(ref _lastId), title.Trim(), Archived: false);
        _records[record.Id] = record;
        return record;
    }

    /// <summary>Marks a record archived; false when it does not exist.</summary>
    public bool MarkArchived(long id)
    {
        if (!_records.TryGetValue(id, out var record))
        {
            return false;
        }

        _records[id] = record with { Archived = true };
        return true;
    }
}
