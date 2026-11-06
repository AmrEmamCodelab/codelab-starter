using Codelab.Security.Replay;
using Codelab.Security.Tickets;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using Starter.Modules.Attachments.Application;
using Starter.Modules.Attachments.Infrastructure.Options;
using Starter.Modules.Attachments.Infrastructure.Security;
using static Starter.Modules.Attachments.Tests.Infrastructure.InfrastructureFixtures;

namespace Starter.Modules.Attachments.Tests.Infrastructure;

public sealed class UploadTicketIssuerTests
{
    private const int ReplayEntryLimit = 100;
    private static readonly DateTimeOffset Now = new(2026, 11, 6, 10, 0, 0, 500, TimeSpan.Zero);

    private readonly FakeTimeProvider _clock = new(Now);
    private readonly UploadTicketRing _tickets;

    public UploadTicketIssuerTests()
    {
        var ring = new UploadTicketRingOptions { Enabled = true, ActiveKeyId = "t1k" };
        ring.Keys.Add(NewKey("t1k"));
        _tickets = new UploadTicketRing(Options.Create(ring), new MemoryReplayGuard(_clock, ReplayEntryLimit), _clock, Protocol);
    }

    [Fact]
    public async Task The_ticket_opens_once_for_the_upload_purpose_with_the_scope_as_its_subject()
    {
        var issued = Issuer(lifetimeSeconds: 300).Issue(Scope);

        var opened = await _tickets.OpenAsync(UploadTickets.Purpose, issued.Ticket, CancellationToken.None);
        var again = await _tickets.OpenAsync(UploadTickets.Purpose, issued.Ticket, CancellationToken.None);

        Assert.Equal(TicketOpenStatus.Valid, opened.Status);
        Assert.Equal(Scope, opened.Ticket!.Subject);
        Assert.Equal(TicketOpenStatus.AlreadyUsed, again.Status);
    }

    [Fact]
    public async Task The_ticket_does_not_open_for_another_purpose()
    {
        var issued = Issuer(lifetimeSeconds: 300).Issue(Scope);

        var opened = await _tickets.OpenAsync("account-activation", issued.Ticket, CancellationToken.None);

        Assert.Equal(TicketOpenStatus.Invalid, opened.Status);
    }

    [Fact]
    public async Task The_ticket_expires_after_the_configured_lifetime_and_expiresAt_is_whole_seconds_not_later_than_that()
    {
        var issued = Issuer(lifetimeSeconds: 60).Issue(Scope);

        Assert.Equal(new DateTimeOffset(2026, 11, 6, 10, 1, 0, TimeSpan.Zero), issued.ExpiresAt);
        _clock.Advance(TimeSpan.FromSeconds(61));
        var opened = await _tickets.OpenAsync(UploadTickets.Purpose, issued.Ticket, CancellationToken.None);
        Assert.Equal(TicketOpenStatus.Expired, opened.Status);
    }

    [Theory]
    [InlineData("http://attachments.test:5185", "http://attachments.test:5185/api/uploads")]
    [InlineData("http://attachments.test:5185/", "http://attachments.test:5185/api/uploads")]
    [InlineData("https://apps.test/attachment-service", "https://apps.test/attachment-service/api/uploads")]
    public void The_upload_url_is_under_the_service_root(string root, string expected)
    {
        var issued = Issuer(lifetimeSeconds: 300, root: root).Issue(Scope);

        Assert.Equal(expected, issued.UploadUrl);
    }

    [Fact]
    public void The_header_is_the_protocol_upload_ticket_header()
    {
        var issued = Issuer(lifetimeSeconds: 300).Issue(Scope);

        Assert.Equal("X-CODELABTEST-Upload-Ticket", issued.TicketHeader);
    }

    [Theory]
    [InlineData("")]
    [InlineData("  ")]
    public void A_blank_scope_is_a_bug(string scope)
    {
        Assert.Throws<ArgumentException>(() => Issuer(lifetimeSeconds: 300).Issue(scope));
    }

    private UploadTicketIssuer Issuer(int lifetimeSeconds, string root = ServiceRoot) =>
        new(
            _tickets,
            Options.Create(new AttachmentsOptions { ServiceBaseUrl = root, TicketLifetimeSeconds = lifetimeSeconds }),
            Protocol,
            _clock);
}
