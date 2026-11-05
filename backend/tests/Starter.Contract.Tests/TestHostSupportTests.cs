using System.Text.RegularExpressions;
using Codelab.Security.PublicIds;
using Codelab.Security.Signing;
using Starter.Infrastructure.Persistence;
using Starter.Modules.Identity.Infrastructure.Options;
using Starter.Tests.Hosting;

namespace Starter.Contract.Tests;

/// <summary>Unit tests of the shared test-host support (settings and generated accounts); no host, no database.</summary>
public sealed partial class TestHostSupportTests
{
    private const string Connection = "Server=(localdb)\\MSSQLLocalDB;Database=StarterItUnitTest";

    [Fact]
    public void EveryRunGetsFreshAccountsAndKeys()
    {
        var first = StarterTestAccounts.Generate();
        var second = StarterTestAccounts.Generate();

        Assert.NotEqual(first.BreakGlassPassword, second.BreakGlassPassword);
        Assert.NotEqual(first.BreakGlassUserName, second.BreakGlassUserName);
        Assert.NotEqual(first.BreakGlassPassword, first.DevUserPassword);
        Assert.NotEqual(StarterTestAccounts.NewKeyBase64(), StarterTestAccounts.NewKeyBase64());
    }

    [Fact]
    public void GeneratedKeysAreThirtyTwoBytes() =>
        Assert.Equal(StarterTestAccounts.KeyBytes, Convert.FromBase64String(StarterTestAccounts.NewKeyBase64()).Length);

    [Fact]
    public void GeneratedKeyIdsFitTheTokenFormat() => Assert.Matches(KeyIdFormat(), StarterTestAccounts.NewKeyId());

    [Fact]
    public void SettingsCarryTheDatabaseAccountsAndKeyRings()
    {
        var accounts = StarterTestAccounts.Generate();
        var settings = StarterHostSettings.For(new StarterHostOptions(Connection, UsesDatabase: true, ApplyMigrationsOnStartup: true, SecurityEnforcementMode.Off), accounts);

        Assert.Equal(Connection, settings[StarterHostSettings.Key(ConnectionStringsOptions.SectionName, nameof(ConnectionStringsOptions.Starter))]);
        Assert.Equal(accounts.BreakGlassPassword, settings[StarterHostSettings.Key(BreakGlassOptions.SectionName, nameof(BreakGlassOptions.Password))]);
        Assert.Equal(accounts.DevUserName, settings[StarterHostSettings.Key(DevUsersOptions.SectionName, "0", nameof(DevUserOptions.UserName))]);
        Assert.Equal(nameof(SecurityEnforcementMode.Off), settings[StarterHostSettings.Key(RequestSigningOptions.SectionName, nameof(RequestSigningOptions.Mode))]);
        Assert.Equal(
            settings[StarterHostSettings.Key(PublicIdKeyringOptions.SectionName, "ActiveKeyId")],
            settings[StarterHostSettings.Key(PublicIdKeyringOptions.SectionName, "Keys", "0", "KeyId")]);
    }

    [Fact]
    public void TheJwtKeyDiffersPerHost()
    {
        var options = new StarterHostOptions(Connection, UsesDatabase: false, ApplyMigrationsOnStartup: false, SecurityEnforcementMode.Enforce);
        var key = StarterHostSettings.Key(JwtOptions.SectionName, nameof(JwtOptions.SigningKey));

        Assert.NotEqual(StarterHostSettings.For(options, StarterTestAccounts.Generate())[key], StarterHostSettings.For(options, StarterTestAccounts.Generate())[key]);
    }

    [Fact]
    public void TheOpenApiDocumentEndpointIsTheOnlyProbeExclusion() =>
        Assert.Equal([StarterProblems.OpenApiDocumentEndpoint], StarterProblems.Options.ExcludedEndpoints);

    [GeneratedRegex("^[a-z0-9]{1,8}$")]
    private static partial Regex KeyIdFormat();
}
