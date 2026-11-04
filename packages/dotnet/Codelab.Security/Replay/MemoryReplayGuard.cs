using System.Collections.Concurrent;
using Codelab.Security.Storage;

namespace Codelab.Security.Replay;

/// <summary>
/// The in-process replay guard: correct for one process only (a second replica has its own memory). Bounded: when
/// <see cref="ReplayProtectionOptions.MemoryEntryLimit"/> live entries exist it purges expired ones, and if the store
/// is still full it answers <see cref="NonceConsumeOutcome.StoreUnavailable"/> (fail closed) rather than evicting a
/// live nonce, which would reopen it to replay.
/// </summary>
public sealed class MemoryReplayGuard : IReplayGuard, IExpiredEntryPurger
{
    private readonly ConcurrentDictionary<string, DateTimeOffset> _entries = new(StringComparer.Ordinal);
    private readonly TimeProvider _clock;
    private readonly int _entryLimit;

    /// <summary>Creates the guard.</summary>
    public MemoryReplayGuard(TimeProvider clock, int entryLimit)
    {
        ArgumentNullException.ThrowIfNull(clock);
        ArgumentOutOfRangeException.ThrowIfLessThan(entryLimit, 1);
        _clock = clock;
        _entryLimit = entryLimit;
    }

    /// <inheritdoc />
    public string StoreName => nameof(MemoryReplayGuard);

    /// <summary>The live entry count (tests and diagnostics).</summary>
    public int Count => _entries.Count;

    /// <inheritdoc />
    public Task<NonceConsumeOutcome> TryConsumeAsync(ReplayNonce nonce, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(nonce);
        var now = _clock.GetUtcNow();
        if (nonce.ExpiresAt <= now)
        {
            return Task.FromResult(NonceConsumeOutcome.Replayed);
        }

        if (_entries.Count >= _entryLimit && Purge(now) == 0 && _entries.Count >= _entryLimit)
        {
            return Task.FromResult(NonceConsumeOutcome.StoreUnavailable);
        }

        return Task.FromResult(Consume(Convert.ToHexString(nonce.KeyHash()), nonce.ExpiresAt, now));
    }

    /// <inheritdoc />
    public Task<int> PurgeExpiredAsync(CancellationToken cancellationToken) => Task.FromResult(Purge(_clock.GetUtcNow()));

    private NonceConsumeOutcome Consume(string key, DateTimeOffset expiresAt, DateTimeOffset now)
    {
        if (_entries.TryAdd(key, expiresAt))
        {
            return NonceConsumeOutcome.Fresh;
        }

        // An expired entry not yet purged may be reclaimed, but only by one caller (compare-and-swap).
        return _entries.TryGetValue(key, out var existing) && existing <= now && _entries.TryUpdate(key, expiresAt, existing)
            ? NonceConsumeOutcome.Fresh
            : NonceConsumeOutcome.Replayed;
    }

    private int Purge(DateTimeOffset now)
    {
        var removed = 0;
        foreach (var entry in _entries)
        {
            if (entry.Value <= now && _entries.TryRemove(entry))
            {
                removed++;
            }
        }

        return removed;
    }
}
