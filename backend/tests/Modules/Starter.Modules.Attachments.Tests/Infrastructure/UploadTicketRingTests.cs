using Codelab.Security.Keys;
using Codelab.Security.Replay;
using Codelab.Security.Tickets;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using Starter.Modules.Attachments.Application;
using Starter.Modules.Attachments.Infrastructure.Options;
using Starter.Modules.Attachments.Infrastructure.Security;
using static Starter.Modules.Attachments.Tests.Infrastructure.InfrastructureFixtures;

namespace Starter.Modules.Attachments.Tests.Infrastructure;

/// <summary>
/// The upload-ticket ring is its own ring: the attachment service holds only it, so it must never open the API's SSO or
/// activation tickets, and the API's own ring must never open an upload ticket.
/// </summary>
public sealed class UploadTicketRingTests
{
    private const int ReplayEntryLimit = 100;
    private const string SsoPendingPurpose = "identity-sso-pending";
    private const string SsoExchangePurpose = "identity-sso-exchange";
    private const string ActivationPurpose = "account-activation";
    private static readonly TimeSpan Lifetime = TimeSpan.FromMinutes(5);

    private readonly FakeTimeProvider _clock = new(new DateTimeOffset(2026, 11, 6, 10, 0, 0, TimeSpan.Zero));

    [Fact]
    public async Task An_upload_ticket_opens_with_the_upload_ring_only()
    {
        var options = UploadOptions("u1");
        var uploads = UploadRing(options);
        var api = ApiRing("t1");
        var ticket = uploads.Seal(new TicketRequest(UploadTickets.Purpose, Scope, Lifetime));

        var byApi = await api.OpenAsync(UploadTickets.Purpose, ticket, CancellationToken.None);
        var byService = await ServiceRing(options).OpenAsync(UploadTickets.Purpose, ticket, CancellationToken.None);

        Assert.Equal(TicketOpenStatus.Invalid, byApi.Status);
        Assert.Equal(TicketOpenStatus.Valid, byService.Status);
    }

    [Theory]
    [InlineData(SsoPendingPurpose)]
    [InlineData(SsoExchangePurpose)]
    [InlineData(ActivationPurpose)]
    public async Task The_upload_ring_never_opens_an_api_ticket(string purpose)
    {
        var uploads = UploadRing(UploadOptions("u1"));
        var api = ApiRing("t1");
        var ticket = api.Seal(new TicketRequest(purpose, "user-1", Lifetime));

        var opened = await uploads.OpenAsync(purpose, ticket, CancellationToken.None);

        Assert.Equal(TicketOpenStatus.Invalid, opened.Status);
    }

    [Fact]
    public void A_disabled_ring_refuses_to_build_naming_its_section()
    {
        var options = UploadOptions("u1");
        options.Enabled = false;

        var failure = Assert.Throws<InvalidOperationException>(() => UploadRing(options));

        Assert.Contains(UploadTicketRingOptions.SectionName, failure.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void The_ring_maps_to_sealed_ticket_options_unchanged()
    {
        var options = UploadOptions("u1");
        options.MaxLifetimeSeconds = 900;

        var mapped = options.ToSealedTicketOptions();

        Assert.True(mapped.Enabled);
        Assert.Equal(900, mapped.MaxLifetimeSeconds);
        Assert.Equal("u1", mapped.ActiveKeyId);
        Assert.Equal(options.Keys[0].MaterialBase64, Assert.Single(mapped.Keys).MaterialBase64);
    }

    [Fact]
    public void A_ticket_longer_than_the_ring_maximum_is_refused()
    {
        var options = UploadOptions("u1");
        options.MaxLifetimeSeconds = 60;

        Assert.Throws<ArgumentOutOfRangeException>(() => UploadRing(options).Seal(new TicketRequest(UploadTickets.Purpose, Scope, Lifetime)));
    }

    private UploadTicketRing UploadRing(UploadTicketRingOptions options) =>
        new(Options.Create(options), new MemoryReplayGuard(_clock, ReplayEntryLimit), _clock, Protocol);

    // The attachment service's only ring: the same keys as the API's upload ring, its own replay guard.
    private SealedTicketService ServiceRing(UploadTicketRingOptions apiUploads) =>
        new(apiUploads.ToSealedTicketOptions(), new MemoryReplayGuard(_clock, ReplayEntryLimit), _clock, Protocol);

    private SealedTicketService ApiRing(string keyId)
    {
        var ring = new SealedTicketOptions { Enabled = true, ActiveKeyId = keyId };
        ring.Keys.Add(NewKey(keyId));
        return new SealedTicketService(ring, new MemoryReplayGuard(_clock, ReplayEntryLimit), _clock, Protocol);
    }

    private static UploadTicketRingOptions UploadOptions(string keyId)
    {
        var ring = new UploadTicketRingOptions { Enabled = true, ActiveKeyId = keyId };
        ring.Keys.Add(NewKey(keyId));
        return ring;
    }
}

public sealed class UploadTicketRingOptionsValidatorTests
{
    [Fact]
    public void A_disabled_ring_passes_without_keys()
    {
        Assert.True(Validate(new UploadTicketRingOptions()).Succeeded);
    }

    [Fact]
    public void An_enabled_ring_with_its_own_keys_passes()
    {
        Assert.True(Validate(Ring("u1"), apiRing: Api("t1")).Succeeded);
    }

    [Fact]
    public void An_enabled_ring_without_keys_fails_naming_its_own_section()
    {
        var result = Validate(new UploadTicketRingOptions { Enabled = true });

        Assert.True(result.Failed);
        Assert.Contains(UploadTicketRingOptions.SectionName, result.FailureMessage, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void A_non_positive_maximum_lifetime_fails(int seconds)
    {
        var ring = Ring("u1");
        ring.MaxLifetimeSeconds = seconds;

        var result = Validate(ring);

        Assert.True(result.Failed);
        Assert.Contains("Codelab:Security:UploadTickets:MaxLifetimeSeconds", result.FailureMessage, StringComparison.Ordinal);
    }

    [Fact]
    public void A_ring_that_reuses_the_api_ticket_key_material_fails()
    {
        var api = Api("t1");
        var ring = new UploadTicketRingOptions { Enabled = true, ActiveKeyId = "u1" };
        ring.Keys.Add(new KeyEntryOptions { KeyId = "u1", MaterialBase64 = api.Keys[0].MaterialBase64 });

        var result = Validate(ring, apiRing: api);

        Assert.True(result.Failed);
        Assert.Contains("Codelab:Security:Tickets", result.FailureMessage, StringComparison.Ordinal);
    }

    private static ValidateOptionsResult Validate(UploadTicketRingOptions ring, SealedTicketOptions? apiRing = null) =>
        new UploadTicketRingOptionsValidator(
                new KeyRingGuard(PublishedMaterialDenylist.Embedded, checkPublished: true),
                Options.Create(apiRing ?? new SealedTicketOptions()))
            .Validate(name: null, ring);

    private static UploadTicketRingOptions Ring(string keyId)
    {
        var ring = new UploadTicketRingOptions { Enabled = true, ActiveKeyId = keyId };
        ring.Keys.Add(NewKey(keyId));
        return ring;
    }

    private static SealedTicketOptions Api(string keyId)
    {
        var ring = new SealedTicketOptions { Enabled = true, ActiveKeyId = keyId };
        ring.Keys.Add(NewKey(keyId));
        return ring;
    }
}
