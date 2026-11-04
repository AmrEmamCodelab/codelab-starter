using Codelab.Security.Keys;
using Codelab.Security.PublicIds;
using Codelab.Security.Storage;
using Codelab.Security.Tests.TestSupport;
using Codelab.Security.Tickets;
using Codelab.Security.Uploads;
using Microsoft.Extensions.Hosting;

namespace Codelab.Security.Tests.Hosting;

/// <summary>The fail-fast boot: every misconfiguration and every published key stops the host before it serves.</summary>
[Collection(SharedCodecGroup.Name)]
public sealed class BootGuardTests
{
    [Fact]
    public async Task A_well_configured_host_boots_and_installs_the_codec()
    {
        await using var host = await SecurityTestHost.StartAsync(SecurityTestHost.BaseOptions());

        Assert.True(PublicIdCodecAccessor.IsInstalled);
        Assert.StartsWith("m1_k1_", PublicId<Records>.From(1).ToToken(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_missing_or_invalid_prefix_fails_the_boot()
    {
        var options = SecurityTestHost.BaseOptions();
        options.Settings[$"{SecuritySections.Protocol}:Prefix"] = "bad-prefix";

        await BootAssert.FailsAsync(options, "Prefix");
    }

    [Fact]
    public async Task A_published_publicid_key_fails_the_boot_without_echoing_it()
    {
        var published = TestKeys.NewKey();
        var options = SecurityTestHost.BaseOptions();
        TestSettings.AddMaterial(options.Settings, SecuritySections.PublicIds, "k1", published);
        options.Denylist = PublishedMaterialDenylist.Embedded.Union(PublishedMaterialDenylist.FromDigests(KeyMaterialDigests.Of(published)));

        var failure = await BootAssert.FailsAsync(options, "PUBLISHED key material");

        Assert.DoesNotContain(published, failure.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(SecuritySections.Tickets, "Enabled")]
    [InlineData(SecuritySections.Uploads, "ReceiptsEnabled")]
    public async Task A_published_ticket_or_receipt_key_fails_the_boot(string section, string switchName)
    {
        var published = TestKeys.NewKey();
        var options = SecurityTestHost.BaseOptions();
        options.Settings[$"{section}:{switchName}"] = "true";
        TestSettings.AddMaterial(options.Settings, section, "x1", published);
        options.Denylist = PublishedMaterialDenylist.FromDigests(KeyMaterialDigests.Of(published));

        await BootAssert.FailsAsync(options, "PUBLISHED");
    }

    [Fact]
    public async Task The_published_material_check_cannot_be_switched_off_outside_development()
    {
        var production = SecurityTestHost.BaseOptions();
        production.Settings[$"{SecuritySections.KeyGuards}:ValidateNotPublishedMaterial"] = "false";
        var development = SecurityTestHost.BaseOptions(Environments.Development);
        development.Settings[$"{SecuritySections.KeyGuards}:ValidateNotPublishedMaterial"] = "false";

        await BootAssert.FailsAsync(production, "ValidateNotPublishedMaterial");
        await BootAssert.SucceedsAsync(development);
    }

    [Fact]
    public async Task A_weak_or_malformed_publicid_ring_fails_the_boot()
    {
        var weak = SecurityTestHost.BaseOptions();
        TestSettings.AddMaterial(weak.Settings, SecuritySections.PublicIds, "k1", Convert.ToBase64String(new byte[8]));
        var badId = SecurityTestHost.BaseOptions();
        TestSettings.AddMaterial(badId.Settings, SecuritySections.PublicIds, "Key_1", TestKeys.NewKey());

        await BootAssert.FailsAsync(weak, "8 bytes");
        await BootAssert.FailsAsync(badId, "KeyId");
    }

    [Fact]
    public async Task A_sql_store_without_a_connection_string_fails_the_boot()
    {
        var options = SecurityTestHost.BaseOptions();
        options.Settings[$"{SecuritySections.Sessions}:Store"] = nameof(SecurityStoreKind.SqlServer);

        await BootAssert.FailsAsync(options, "ConnectionString");
    }

    [Fact]
    public async Task Disabled_features_refuse_to_hand_out_their_service()
    {
        await using var host = await SecurityTestHost.StartAsync(SecurityTestHost.BaseOptions());

        Assert.Throws<InvalidOperationException>(() => host.Services.GetRequiredService<ISealedTicketService>());
        Assert.Throws<InvalidOperationException>(() => host.Services.GetRequiredService<IUploadReceiptSigner>());
        Assert.NotNull(host.Services.GetRequiredService<FileSignatureSniffer>());
    }

    [Fact]
    public async Task The_purge_job_hook_runs_every_registered_store()
    {
        await using var host = await SecurityTestHost.StartAsync(SecurityTestHost.BaseOptions());

        var removed = await host.Services.GetRequiredService<SecurityStorePurgeJob>().RunAsync(CancellationToken.None);

        Assert.Contains(nameof(Codelab.Security.Replay.MemoryReplayGuard), removed.Keys);
        Assert.Contains(nameof(Codelab.Security.Sessions.MemorySessionRevocationStore), removed.Keys);
    }

    [Fact]
    public async Task An_enabled_ticket_service_resolves_and_round_trips()
    {
        var options = SecurityTestHost.BaseOptions();
        options.Settings[$"{SecuritySections.Tickets}:Enabled"] = "true";
        TestSettings.AddRing(options.Settings, SecuritySections.Tickets, "t1");
        await using var host = await SecurityTestHost.StartAsync(options);
        var tickets = host.Services.GetRequiredService<ISealedTicketService>();

        var opened = await tickets.OpenAsync("activation", tickets.Seal(new TicketRequest("activation", "u1", TimeSpan.FromMinutes(5))), CancellationToken.None);

        Assert.Equal(TicketOpenStatus.Valid, opened.Status);
    }
}
