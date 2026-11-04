using Codelab.Security.ServiceAuth;
using Codelab.Security.Tests.TestSupport;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;

namespace Codelab.Security.Tests.Hosting;

/// <summary>
/// The per-process memory stores outside Development: a revoked sid, a consumed jti or an opened ticket is known to one
/// process only, so a second replica would accept it. Every user of a memory store needs the single-instance
/// acknowledgement (or SqlServer), not only request signing in Enforce.
/// </summary>
[Collection(SharedCodecGroup.Name)]
public sealed class SingleInstanceStoreGuardTests
{
    private const string Acknowledgement = $"{SecuritySections.ReplayProtection}:AcknowledgeSingleInstance";
    private const string SessionStoreRefusal = $"{SecuritySections.Sessions}:Store is the per-process memory store";
    private const string ServiceAssertionUser = "the service-assertion server";
    private const string TicketUser = "sealed tickets";

    [Fact]
    public async Task Session_revocation_on_the_memory_store_in_production_needs_the_acknowledgement()
    {
        await BootAssert.FailsAsync(Unacknowledged(SecurityTestHost.BaseOptions()), SessionStoreRefusal);
    }

    [Fact]
    public async Task The_service_assertion_server_on_the_memory_replay_store_needs_the_acknowledgement()
    {
        await BootAssert.FailsAsync(Unacknowledged(WithServiceAssertionServer(SecurityTestHost.BaseOptions())), ServiceAssertionUser);
    }

    [Fact]
    public async Task Sealed_tickets_on_the_memory_replay_store_need_the_acknowledgement()
    {
        var options = SecurityTestHost.BaseOptions();
        options.Settings[$"{SecuritySections.Tickets}:Enabled"] = bool.TrueString;
        TestSettings.AddRing(options.Settings, SecuritySections.Tickets, "t1");

        await BootAssert.FailsAsync(Unacknowledged(options), TicketUser);
    }

    [Fact]
    public async Task The_acknowledgement_lets_a_single_instance_production_host_boot()
    {
        await BootAssert.SucceedsAsync(WithServiceAssertionServer(SecurityTestHost.BaseOptions()));
    }

    [Fact]
    public async Task Development_needs_no_acknowledgement()
    {
        await BootAssert.SucceedsAsync(Unacknowledged(WithServiceAssertionServer(SecurityTestHost.BaseOptions(Environments.Development))));
    }

    private static TestHostOptions Unacknowledged(TestHostOptions options)
    {
        options.Settings[Acknowledgement] = bool.FalseString;
        return options;
    }

    private static TestHostOptions WithServiceAssertionServer(TestHostOptions options)
    {
        var section = SecuritySections.ServiceAssertionServer;
        options.Settings[$"{section}:Audience"] = "starter-api";
        options.Settings[$"{section}:TrustedServices:0:ServiceName"] = "starter-worker";
        options.Settings[$"{section}:TrustedServices:0:KeyId"] = "s1";
        options.Settings[$"{section}:TrustedServices:0:PublicKeyPem"] = TestKeys.NewEcKeyPair().PublicPem;
        var settings = options.Settings;
        options.ConfigureServices = services => services.AddAuthentication().AddCodelabServiceAssertion(new ConfigurationBuilder().AddInMemoryCollection(settings).Build());
        return options;
    }
}
