using System.Collections.Concurrent;
using Codelab.Security.Storage;

namespace Codelab.Security.Sessions;

/// <summary>
/// The per-user session version: tokens carry the version they were issued under (<c>sver</c>); bumping it (password
/// change, deactivation, permission change) invalidates every outstanding session of that user at once.
/// A user never bumped is at version 0.
/// </summary>
public interface ISessionVersionStore
{
    /// <summary>The current version of <paramref name="userId"/> (0 when never bumped).</summary>
    Task<long> GetVersionAsync(string userId, CancellationToken cancellationToken);

    /// <summary>Increments and returns the version of <paramref name="userId"/> (atomic).</summary>
    Task<long> BumpAsync(string userId, CancellationToken cancellationToken);
}

/// <summary>A session id revoked until <see cref="ExpiresAt"/> (set it to the session's own expiry; then it can be purged).</summary>
public sealed record SessionRevocation(string SessionId, DateTimeOffset ExpiresAt);

/// <summary>The per-session (<c>sid</c>) revocation list, checked on every authenticated request.</summary>
public interface ISessionRevocationStore
{
    /// <summary>Revokes a session (idempotent; a second revoke keeps the later expiry).</summary>
    Task RevokeAsync(SessionRevocation revocation, CancellationToken cancellationToken);

    /// <summary>True when <paramref name="sessionId"/> is revoked and the revocation has not expired.</summary>
    Task<bool> IsRevokedAsync(string sessionId, CancellationToken cancellationToken);
}

/// <summary>In-process session versions (single process; lost on restart).</summary>
public sealed class MemorySessionVersionStore : ISessionVersionStore
{
    private readonly ConcurrentDictionary<string, long> _versions = new(StringComparer.Ordinal);

    /// <inheritdoc />
    public Task<long> GetVersionAsync(string userId, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(userId);
        return Task.FromResult(_versions.GetValueOrDefault(userId));
    }

    /// <inheritdoc />
    public Task<long> BumpAsync(string userId, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(userId);
        return Task.FromResult(_versions.AddOrUpdate(userId, 1, static (_, current) => current + 1));
    }
}

/// <summary>In-process revoked sessions with expiry (single process; lost on restart).</summary>
public sealed class MemorySessionRevocationStore(TimeProvider clock) : ISessionRevocationStore, IExpiredEntryPurger
{
    private readonly ConcurrentDictionary<string, DateTimeOffset> _revoked = new(StringComparer.Ordinal);

    /// <inheritdoc />
    public string StoreName => nameof(MemorySessionRevocationStore);

    /// <inheritdoc />
    public Task RevokeAsync(SessionRevocation revocation, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(revocation);
        ArgumentException.ThrowIfNullOrWhiteSpace(revocation.SessionId, nameof(revocation));
        _revoked.AddOrUpdate(revocation.SessionId, revocation.ExpiresAt, (_, current) => current > revocation.ExpiresAt ? current : revocation.ExpiresAt);
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task<bool> IsRevokedAsync(string sessionId, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sessionId);
        return Task.FromResult(_revoked.TryGetValue(sessionId, out var expiresAt) && expiresAt > clock.GetUtcNow());
    }

    /// <inheritdoc />
    public Task<int> PurgeExpiredAsync(CancellationToken cancellationToken)
    {
        var now = clock.GetUtcNow();
        var removed = _revoked.Count(entry => entry.Value <= now && _revoked.TryRemove(entry));
        return Task.FromResult(removed);
    }
}
