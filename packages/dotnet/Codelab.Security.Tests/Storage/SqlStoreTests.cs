using Codelab.Security.Replay;
using Codelab.Security.Sessions;
using Codelab.Security.Storage;
using Codelab.Security.Tests.TestSupport;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;

namespace Codelab.Security.Tests.Storage;

/// <summary>The SQL stores against a real SQL Server, in a database this class creates and drops.</summary>
public sealed class SqlStoreTests(SqlTestDatabase database) : IClassFixture<SqlTestDatabase>
{
    private readonly FakeTimeProvider _clock = new(new DateTimeOffset(2026, 11, 4, 10, 0, 0, TimeSpan.Zero));

    private SqlReplayGuard ReplayGuard() => new(database.StoreOptions(), _clock, NullLogger<SqlReplayGuard>.Instance);

    [Fact]
    public async Task The_schema_script_is_idempotent()
    {
        await SecuritySqlSchema.EnsureCreatedAsync(database.ConnectionString, CancellationToken.None);
        await SecuritySqlSchema.EnsureCreatedAsync(database.ConnectionString, CancellationToken.None);

        Assert.True(await database.CountAsync(SecuritySqlSchema.ReplayNonceTable) >= 0);
        Assert.Contains("IF OBJECT_ID", SecuritySqlSchema.ReadScript(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_nonce_is_fresh_once_then_replayed_across_two_guard_instances()
    {
        var nonce = new ReplayNonce("7.a", Guid.NewGuid().ToString("N"), _clock.GetUtcNow().AddMinutes(3));

        Assert.Equal(NonceConsumeOutcome.Fresh, await ReplayGuard().TryConsumeAsync(nonce, CancellationToken.None));
        Assert.Equal(NonceConsumeOutcome.Replayed, await ReplayGuard().TryConsumeAsync(nonce, CancellationToken.None));
    }

    [Fact]
    public async Task Concurrent_consumers_across_connections_yield_exactly_one_fresh()
    {
        var nonce = new ReplayNonce("7.a", Guid.NewGuid().ToString("N"), _clock.GetUtcNow().AddMinutes(3));
        var guard = ReplayGuard();

        var outcomes = await Task.WhenAll(Enumerable.Range(0, 16).Select(_ => guard.TryConsumeAsync(nonce, CancellationToken.None)));

        Assert.Single(outcomes, outcome => outcome == NonceConsumeOutcome.Fresh);
    }

    [Fact]
    public async Task An_unreachable_store_fails_closed_as_unavailable()
    {
        var broken = new SqlStoreOptions { ConnectionString = "Server=127.0.0.1,1;Integrated Security=true;Connect Timeout=1;TrustServerCertificate=true", CommandTimeoutSeconds = 1 };
        var guard = new SqlReplayGuard(broken, _clock, NullLogger<SqlReplayGuard>.Instance);

        var outcome = await guard.TryConsumeAsync(new ReplayNonce("s", "n", _clock.GetUtcNow().AddMinutes(1)), CancellationToken.None);

        Assert.Equal(NonceConsumeOutcome.StoreUnavailable, outcome);
    }

    [Fact]
    public async Task Purge_deletes_expired_nonces_only()
    {
        var guard = ReplayGuard();
        var keep = new ReplayNonce("purge", Guid.NewGuid().ToString("N"), _clock.GetUtcNow().AddHours(1));
        await guard.TryConsumeAsync(new ReplayNonce("purge", Guid.NewGuid().ToString("N"), _clock.GetUtcNow().AddSeconds(5)), CancellationToken.None);
        await guard.TryConsumeAsync(keep, CancellationToken.None);
        _clock.Advance(TimeSpan.FromSeconds(10));

        Assert.True(await guard.PurgeExpiredAsync(CancellationToken.None) >= 1);
        Assert.Equal(NonceConsumeOutcome.Replayed, await guard.TryConsumeAsync(keep, CancellationToken.None));
    }

    [Fact]
    public async Task Session_versions_start_at_zero_and_bump_atomically()
    {
        var store = new SqlSessionVersionStore(database.StoreOptions(), _clock);
        var user = Guid.NewGuid().ToString("N");

        Assert.Equal(0, await store.GetVersionAsync(user, CancellationToken.None));
        var bumps = await Task.WhenAll(Enumerable.Range(0, 8).Select(_ => store.BumpAsync(user, CancellationToken.None)));

        Assert.Equal(Enumerable.Range(1, 8).Select(value => (long)value), bumps.Order());
        Assert.Equal(8, await store.GetVersionAsync(user, CancellationToken.None));
    }

    [Fact]
    public async Task A_revoked_session_stays_revoked_until_its_expiry_then_purges()
    {
        var store = new SqlSessionRevocationStore(database.StoreOptions(), _clock);
        var sid = Guid.NewGuid().ToString("N");
        await store.RevokeAsync(new SessionRevocation(sid, _clock.GetUtcNow().AddMinutes(30)), CancellationToken.None);
        await store.RevokeAsync(new SessionRevocation(sid, _clock.GetUtcNow().AddMinutes(5)), CancellationToken.None);

        Assert.True(await store.IsRevokedAsync(sid, CancellationToken.None));
        Assert.False(await store.IsRevokedAsync(Guid.NewGuid().ToString("N"), CancellationToken.None));
        _clock.Advance(TimeSpan.FromMinutes(31));
        Assert.False(await store.IsRevokedAsync(sid, CancellationToken.None));
        Assert.True(await store.PurgeExpiredAsync(CancellationToken.None) >= 1);
    }

    [Fact]
    public async Task The_purge_job_runs_every_store()
    {
        var job = new SecurityStorePurgeJob([ReplayGuard(), new SqlSessionRevocationStore(database.StoreOptions(), _clock)]);

        var removed = await job.RunAsync(CancellationToken.None);

        Assert.Equal([nameof(SqlReplayGuard), nameof(SqlSessionRevocationStore)], removed.Keys.Order(StringComparer.Ordinal));
    }
}
