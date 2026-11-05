using System.Collections.Concurrent;
using System.Security.Cryptography;
using Microsoft.IdentityModel.Tokens;

namespace Starter.DevIdp.Grants;

/// <summary>
/// An in-memory store of values under random, unguessable handles (256 bits, base64url) with an expiry read from
/// <see cref="TimeProvider"/>. <see cref="Take"/> is one-time (the handle is removed even when it has expired);
/// <see cref="Find"/> reads without removing. Expired entries are swept on every <see cref="Add"/>. Thread-safe;
/// everything is lost when the IdP stops.
/// </summary>
/// <typeparam name="T">The stored value.</typeparam>
internal sealed class ExpiringStore<T>
    where T : class
{
    private const int HandleBytes = 32;

    private readonly ConcurrentDictionary<string, Entry> _entries = new(StringComparer.Ordinal);
    private readonly TimeProvider _timeProvider;

    /// <summary>Creates an empty store on <paramref name="timeProvider"/>.</summary>
    public ExpiringStore(TimeProvider timeProvider) => _timeProvider = timeProvider;

    /// <summary>The number of entries held, expired or not (tests).</summary>
    public int Count => _entries.Count;

    /// <summary>Stores <paramref name="value"/> for <paramref name="lifetime"/> and returns its new handle.</summary>
    public string Add(T value, TimeSpan lifetime)
    {
        var now = _timeProvider.GetUtcNow();
        Sweep(now);
        var handle = Base64UrlEncoder.Encode(RandomNumberGenerator.GetBytes(HandleBytes));
        _entries[handle] = new Entry(value, ExpiresAt: now + lifetime);
        return handle;
    }

    /// <summary>Removes the entry under <paramref name="handle"/> and returns its value when it had not expired.</summary>
    public T? Take(string? handle)
    {
        if (handle is null || !_entries.TryRemove(handle, out var entry))
        {
            return null;
        }

        return IsLive(entry, _timeProvider.GetUtcNow()) ? entry.Value : null;
    }

    /// <summary>The value under <paramref name="handle"/> when it has not expired, without removing it.</summary>
    public T? Find(string? handle)
    {
        if (handle is null || !_entries.TryGetValue(handle, out var entry))
        {
            return null;
        }

        return IsLive(entry, _timeProvider.GetUtcNow()) ? entry.Value : null;
    }

    private static bool IsLive(Entry entry, DateTimeOffset now) => now < entry.ExpiresAt;

    private void Sweep(DateTimeOffset now)
    {
        foreach (var (handle, entry) in _entries)
        {
            if (!IsLive(entry, now))
            {
                _entries.TryRemove(handle, out _);
            }
        }
    }

    private sealed record Entry(T Value, DateTimeOffset ExpiresAt);
}
