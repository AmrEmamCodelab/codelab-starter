using Codelab.Security.Replay;
using Codelab.Security.Tests.TestSupport;
using Codelab.Security.Tickets;
using Microsoft.Extensions.Time.Testing;

namespace Codelab.Security.Tests.Tickets;

public sealed class SealedTicketTests
{
    private const string Purpose = "account-activation";
    private readonly FakeTimeProvider _clock = new(new DateTimeOffset(2026, 11, 4, 10, 0, 0, TimeSpan.Zero));
    private readonly SealedTicketOptions _options = TestKeys.Ring(new SealedTicketOptions { Enabled = true, MaxLifetimeSeconds = 3600 }, "t1");

    private SealedTicketService NewService(SecurityProtocol? protocol = null, SealedTicketOptions? options = null) =>
        new(options ?? _options, new MemoryReplayGuard(_clock, entryLimit: 100), _clock, protocol ?? TestProtocols.Test);

    private static TicketRequest Request(TimeSpan lifetime) => new(Purpose, "user-7", lifetime) { Data = "{\"org\":3}" };

    [Fact]
    public async Task A_ticket_opens_once_with_its_subject_and_data()
    {
        var service = NewService();
        var token = service.Seal(Request(TimeSpan.FromMinutes(15)));

        var first = await service.OpenAsync(Purpose, token, CancellationToken.None);
        var second = await service.OpenAsync(Purpose, token, CancellationToken.None);

        Assert.Equal(TicketOpenStatus.Valid, first.Status);
        Assert.Equal("user-7", first.Ticket!.Subject);
        Assert.Equal("{\"org\":3}", first.Ticket.Data);
        Assert.Equal(TicketOpenStatus.AlreadyUsed, second.Status);
        Assert.DoesNotContain("user-7", token, StringComparison.Ordinal);
    }

    [Fact]
    public async Task An_expired_ticket_is_refused_by_the_clock()
    {
        var service = NewService();
        var token = service.Seal(Request(TimeSpan.FromMinutes(1)));

        _clock.Advance(TimeSpan.FromMinutes(2));

        Assert.Equal(TicketOpenStatus.Expired, (await service.OpenAsync(Purpose, token, CancellationToken.None)).Status);
    }

    [Fact]
    public async Task A_ticket_never_opens_for_another_purpose_another_prefix_or_after_tampering()
    {
        var service = NewService();
        var token = service.Seal(Request(TimeSpan.FromMinutes(15)));
        var tampered = token[..^2] + (token[^2] == 'A' ? 'B' : 'A') + token[^1];

        Assert.Equal(TicketOpenStatus.Invalid, (await service.OpenAsync("password-reset", token, CancellationToken.None)).Status);
        Assert.Equal(TicketOpenStatus.Invalid, (await NewService(TestProtocols.Alternate).OpenAsync(Purpose, token, CancellationToken.None)).Status);
        Assert.Equal(TicketOpenStatus.Invalid, (await service.OpenAsync(Purpose, tampered, CancellationToken.None)).Status);
        Assert.Equal(TicketOpenStatus.Invalid, (await service.OpenAsync(Purpose, "t1.t1.short", CancellationToken.None)).Status);
        Assert.Equal(TicketOpenStatus.Valid, (await service.OpenAsync(Purpose, token, CancellationToken.None)).Status);
    }

    [Fact]
    public async Task A_ticket_sealed_under_a_key_outside_the_ring_is_invalid()
    {
        var foreign = NewService(options: TestKeys.Ring(new SealedTicketOptions { Enabled = true }, "t1"));

        var outcome = await NewService().OpenAsync(Purpose, foreign.Seal(Request(TimeSpan.FromMinutes(5))), CancellationToken.None);

        Assert.Equal(TicketOpenStatus.Invalid, outcome.Status);
    }

    [Fact]
    public void A_lifetime_beyond_the_maximum_or_not_positive_is_refused()
    {
        var service = NewService();

        Assert.Throws<ArgumentOutOfRangeException>(() => service.Seal(Request(TimeSpan.FromHours(2))));
        Assert.Throws<ArgumentOutOfRangeException>(() => service.Seal(Request(TimeSpan.Zero)));
        Assert.Throws<ArgumentException>(() => service.Seal(new TicketRequest(" ", "u", TimeSpan.FromMinutes(1))));
    }

    [Fact]
    public async Task A_store_that_cannot_answer_fails_closed()
    {
        var service = new SealedTicketService(_options, new UnavailableGuard(), _clock, TestProtocols.Test);

        var outcome = await service.OpenAsync(Purpose, service.Seal(Request(TimeSpan.FromMinutes(5))), CancellationToken.None);

        Assert.Equal(TicketOpenStatus.StoreUnavailable, outcome.Status);
        Assert.Null(outcome.Ticket);
    }

    private sealed class UnavailableGuard : IReplayGuard
    {
        public Task<NonceConsumeOutcome> TryConsumeAsync(ReplayNonce nonce, CancellationToken cancellationToken) => Task.FromResult(NonceConsumeOutcome.StoreUnavailable);
    }
}
