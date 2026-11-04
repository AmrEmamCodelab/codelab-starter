using Codelab.Security.Replay;
using Microsoft.Extensions.Time.Testing;

namespace Codelab.Security.Tests.Replay;

public sealed class MemoryReplayGuardTests
{
    private const int EntryLimit = 1000;
    private readonly FakeTimeProvider _clock = new(new DateTimeOffset(2026, 11, 4, 10, 0, 0, TimeSpan.Zero));

    private MemoryReplayGuard NewGuard(int entryLimit = EntryLimit) => new(_clock, entryLimit);

    private ReplayNonce Nonce(string scope, string nonce) => new(scope, nonce, _clock.GetUtcNow().AddMinutes(3));

    [Fact]
    public async Task A_fresh_nonce_is_consumed_exactly_once()
    {
        var guard = NewGuard();

        Assert.Equal(NonceConsumeOutcome.Fresh, await guard.TryConsumeAsync(Nonce("7.a", "n1"), CancellationToken.None));
        Assert.Equal(NonceConsumeOutcome.Replayed, await guard.TryConsumeAsync(Nonce("7.a", "n1"), CancellationToken.None));
    }

    [Fact]
    public async Task Different_nonces_and_different_scopes_are_independent()
    {
        var guard = NewGuard();
        await guard.TryConsumeAsync(Nonce("7.a", "n1"), CancellationToken.None);

        Assert.Equal(NonceConsumeOutcome.Fresh, await guard.TryConsumeAsync(Nonce("7.a", "n2"), CancellationToken.None));
        Assert.Equal(NonceConsumeOutcome.Fresh, await guard.TryConsumeAsync(Nonce("7.b", "n1"), CancellationToken.None));
    }

    [Fact]
    public async Task Scope_and_nonce_boundaries_cannot_be_shifted()
    {
        var guard = NewGuard();
        await guard.TryConsumeAsync(Nonce("7.a", "bc"), CancellationToken.None);

        Assert.Equal(NonceConsumeOutcome.Fresh, await guard.TryConsumeAsync(Nonce("7.ab", "c"), CancellationToken.None));
    }

    [Fact]
    public async Task An_already_expired_nonce_is_never_reported_fresh()
    {
        var expired = new ReplayNonce("7.a", "n1", _clock.GetUtcNow());

        Assert.Equal(NonceConsumeOutcome.Replayed, await NewGuard().TryConsumeAsync(expired, CancellationToken.None));
    }

    [Fact]
    public async Task Concurrent_consumers_of_one_nonce_yield_exactly_one_fresh()
    {
        var guard = NewGuard();
        var nonce = Nonce("7.a", "race");

        var outcomes = await Task.WhenAll(Enumerable.Range(0, 64).Select(_ => Task.Run(() => guard.TryConsumeAsync(nonce, CancellationToken.None))));

        Assert.Single(outcomes, outcome => outcome == NonceConsumeOutcome.Fresh);
    }

    [Fact]
    public async Task A_full_store_purges_expired_entries_and_otherwise_fails_closed()
    {
        var guard = NewGuard(entryLimit: 2);
        await guard.TryConsumeAsync(new ReplayNonce("s", "1", _clock.GetUtcNow().AddSeconds(10)), CancellationToken.None);
        await guard.TryConsumeAsync(Nonce("s", "2"), CancellationToken.None);

        Assert.Equal(NonceConsumeOutcome.StoreUnavailable, await guard.TryConsumeAsync(Nonce("s", "3"), CancellationToken.None));

        _clock.Advance(TimeSpan.FromSeconds(11));
        Assert.Equal(NonceConsumeOutcome.Fresh, await guard.TryConsumeAsync(Nonce("s", "3"), CancellationToken.None));
    }

    [Fact]
    public async Task Purge_removes_only_expired_entries()
    {
        var guard = NewGuard();
        await guard.TryConsumeAsync(new ReplayNonce("s", "short", _clock.GetUtcNow().AddSeconds(5)), CancellationToken.None);
        await guard.TryConsumeAsync(Nonce("s", "long"), CancellationToken.None);
        _clock.Advance(TimeSpan.FromSeconds(6));

        Assert.Equal(1, await guard.PurgeExpiredAsync(CancellationToken.None));
        Assert.Equal(1, guard.Count);
        Assert.Equal(NonceConsumeOutcome.Replayed, await guard.TryConsumeAsync(Nonce("s", "long"), CancellationToken.None));
    }

    [Fact]
    public async Task An_expired_entry_not_yet_purged_may_be_reused_once()
    {
        var guard = NewGuard();
        await guard.TryConsumeAsync(new ReplayNonce("s", "n", _clock.GetUtcNow().AddSeconds(5)), CancellationToken.None);
        _clock.Advance(TimeSpan.FromSeconds(6));

        Assert.Equal(NonceConsumeOutcome.Fresh, await guard.TryConsumeAsync(Nonce("s", "n"), CancellationToken.None));
        Assert.Equal(NonceConsumeOutcome.Replayed, await guard.TryConsumeAsync(Nonce("s", "n"), CancellationToken.None));
    }
}
