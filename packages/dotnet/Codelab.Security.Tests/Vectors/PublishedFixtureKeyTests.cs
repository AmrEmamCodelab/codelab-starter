using System.Security.Cryptography;
using System.Text.Json;
using Codelab.Security.Keys;
using Codelab.Security.ServiceAuth;
using Codelab.Security.Signing;
using Codelab.Security.Tests.Keys;
using Codelab.Security.Tests.TestSupport;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;

namespace Codelab.Security.Tests.Vectors;

/// <summary>
/// The fixture keys are published (they are in this repository), so a production boot must refuse every one of them,
/// exactly like a harvest-source key. The denylist file is written by <c>node tools/vectors/fixture-keys.mjs
/// published-material</c>; these tests prove it covers every fixture key and that the guard reads it.
/// </summary>
[Collection(SharedCodecGroup.Name)]
public sealed class PublishedFixtureKeyTests
{
    private const string FixturesResource = PublishedMaterialDenylist.ResourcePrefix + "published-material.fixtures.json";
    private const string FixturesFile = "packages/dotnet/Codelab.Security/PublishedMaterial/published-material.fixtures.json";
    private const string HarvestDigestFile = "tools/extract/secret-digests.json";
    private const string PublishedRefusal = "PUBLISHED key material";
    private const string LineFeed = "\n";
    private const string CarriageReturnLineFeed = "\r\n";
    private const int HarvestSamplePerKind = 8;

    private static readonly string[] HarvestKeyKinds = ["config-secret", "script-secret", "test-fixture", "vector-key"];

    private static readonly string[] KeyRingSections = [SecuritySections.PublicIds, SecuritySections.RequestSigning, SecuritySections.Tickets, SecuritySections.Uploads];

    public static TheoryData<string, string> FixtureKeyInEveryRing
    {
        get
        {
            var data = new TheoryData<string, string>();
            foreach (var (keyId, _) in FixtureKeySet.Default.SymmetricKeys)
            {
                foreach (var section in KeyRingSections)
                {
                    data.Add(keyId, section);
                }
            }

            return data;
        }
    }

    public static TheoryData<string> HarvestKeyKindNames => new(HarvestKeyKinds);

    [Fact]
    public void The_fixture_denylist_is_embedded()
    {
        Assert.Contains(FixturesResource, PublishedMaterialDenylist.Embedded.Sources);
    }

    [Fact]
    public void Every_fixture_key_in_every_spelling_is_on_the_embedded_denylist()
    {
        var denylist = PublishedMaterialDenylist.Embedded;
        var pem = FixtureKeySet.Default.ServiceAssertion.PrivateKeyPem.Replace(CarriageReturnLineFeed, LineFeed, StringComparison.Ordinal);

        Assert.All(FixtureKeySet.Default.SymmetricKeys, key => Assert.True(denylist.IsPublished(key.Material), $"Fixture key {key.KeyId} is not on the denylist; run node tools/vectors/fixture-keys.mjs published-material."));
        Assert.True(denylist.IsPublished(pem));
        Assert.True(denylist.IsPublished(pem.Replace(LineFeed, CarriageReturnLineFeed, StringComparison.Ordinal)));
        Assert.True(denylist.IsPublished(pem.TrimEnd()));
    }

    [Fact]
    public void The_fixture_denylist_file_holds_digests_of_digests_only()
    {
        using var document = JsonDocument.Parse(File.ReadAllText(PublishedMaterialTests.RepositoryFile(FixturesFile)));
        var entries = document.RootElement.GetProperty(PublishedMaterialDenylist.EntriesProperty).EnumerateArray().Select(entry => entry.GetString()!).ToList();
        var materials = FixtureKeySet.Default.SymmetricKeys.Select(key => key.Material).ToList();

        Assert.All(entries, entry => Assert.Matches("^[0-9a-f]{64}$", entry));
        Assert.All(materials, material => Assert.DoesNotContain(entries, entry => material.Contains(entry, StringComparison.OrdinalIgnoreCase)));
        Assert.All(materials.SelectMany(KeyMaterialDigests.Of), digest => Assert.DoesNotContain(digest, entries));
    }

    [Fact]
    public void The_fixture_keys_are_fresh_not_harvest_source_keys()
    {
        var harvest = HarvestDigests(kinds: null).ToHashSet(StringComparer.Ordinal);
        var fixture = FixtureKeySet.Default;
        var values = fixture.SymmetricKeys.Select(key => key.Material).Append(fixture.ServiceAssertion.PrivateKeyPem).Append(fixture.ServiceAssertion.PublicKeyPem);

        Assert.All(values.SelectMany(KeyMaterialDigests.Of), digest => Assert.DoesNotContain(digest, harvest));
    }

    [Theory]
    [MemberData(nameof(HarvestKeyKindNames))]
    public void Harvest_source_key_digests_are_refused_by_the_embedded_denylist(string kind)
    {
        // Digest matching only: the harvest key values are never stored anywhere in this repository.
        var sample = HarvestDigests([kind]).Take(HarvestSamplePerKind).ToList();

        Assert.NotEmpty(sample);
        Assert.All(sample, digest => Assert.True(PublishedMaterialDenylist.Embedded.ContainsDigest(digest)));
    }

    [Theory]
    [MemberData(nameof(FixtureKeyInEveryRing))]
    public async Task A_production_boot_with_a_fixture_key_fails_fast(string keyId, string section)
    {
        var material = FixtureMaterial(keyId);
        var options = FixtureBoot.Options(section, material);

        var failure = await BootAssert.FailsAsync(options, PublishedRefusal);

        Assert.DoesNotContain(material, failure.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task A_production_boot_with_the_fixture_service_assertion_key_fails_fast(bool crlf)
    {
        var pem = FixtureKeySet.Default.ServiceAssertion.PrivateKeyPem.Replace(CarriageReturnLineFeed, LineFeed, StringComparison.Ordinal);
        var options = FixtureBoot.ServiceAssertionClient(crlf ? pem.Replace(LineFeed, CarriageReturnLineFeed, StringComparison.Ordinal) : pem);

        await BootAssert.FailsAsync(options, PublishedRefusal);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task A_production_boot_whose_server_trusts_the_fixture_public_key_fails_fast(bool crlf)
    {
        var pem = FixtureKeySet.Default.ServiceAssertion.PublicKeyPem.Replace(CarriageReturnLineFeed, LineFeed, StringComparison.Ordinal);
        var options = FixtureBoot.ServiceAssertionServer(crlf ? pem.Replace(LineFeed, CarriageReturnLineFeed, StringComparison.Ordinal) : pem);

        var failure = await BootAssert.FailsAsync(options, PublishedRefusal);

        Assert.Contains(SecuritySections.ServiceAssertionServer, failure.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_production_boot_whose_server_trusts_a_fresh_public_key_boots()
    {
        await BootAssert.SucceedsAsync(FixtureBoot.ServiceAssertionServer(TestKeys.NewEcKeyPair().PublicPem));
    }

    public static TheoryData<string> FixturePemRespellings
    {
        get
        {
            var fixture = FixtureKeySet.Default.ServiceAssertion;
            using var key = ECDsa.Create();
            key.ImportFromPem(fixture.PrivateKeyPem);
            return new TheoryData<string>(
                OneLineBody(fixture.PrivateKeyPem),
                LineFeed + fixture.PrivateKeyPem,
                "  " + fixture.PrivateKeyPem + LineFeed + LineFeed,
                key.ExportECPrivateKeyPem(),
                key.ExportPkcs8PrivateKeyPem(),
                Convert.ToBase64String(key.ExportECPrivateKey()),
                OneLineBody(fixture.PublicKeyPem),
                key.ExportSubjectPublicKeyInfoPem());
        }
    }

    [Theory]
    [MemberData(nameof(FixturePemRespellings))]
    public void Every_respelling_of_the_fixture_key_pair_is_on_the_embedded_denylist(string pem)
    {
        Assert.True(PublishedMaterialDenylist.Embedded.IsPublished(pem));
    }

    [Fact]
    public async Task A_production_boot_with_the_fixture_private_key_on_one_line_fails_fast()
    {
        await BootAssert.FailsAsync(FixtureBoot.ServiceAssertionClient(OneLineBody(FixtureKeySet.Default.ServiceAssertion.PrivateKeyPem)), PublishedRefusal);
    }

    [Fact]
    public void A_fresh_key_pair_in_any_spelling_is_not_published()
    {
        var (privatePem, publicPem) = TestKeys.NewEcKeyPair();

        Assert.False(PublishedMaterialDenylist.Embedded.IsPublished(privatePem));
        Assert.False(PublishedMaterialDenylist.Embedded.IsPublished(OneLineBody(publicPem)));
    }

    [Fact]
    public async Task A_fresh_key_in_the_same_configuration_boots()
    {
        await BootAssert.SucceedsAsync(FixtureBoot.Options(SecuritySections.RequestSigning, TestKeys.NewKey()));
    }

    [Fact]
    public async Task Development_may_switch_the_check_off_to_run_the_fixtures_locally()
    {
        var options = FixtureBoot.Options(SecuritySections.PublicIds, FixtureMaterial(FixtureKeySet.Default.PublicIds.ActiveKeyId), Environments.Development);
        options.Settings[$"{SecuritySections.KeyGuards}:{nameof(KeyGuardOptions.ValidateNotPublishedMaterial)}"] = bool.FalseString;

        await BootAssert.SucceedsAsync(options);
    }

    /// <summary>The PEM with its armour lines kept and the base64 body joined onto one line (an environment-variable shape).</summary>
    private static string OneLineBody(string pem)
    {
        var lines = pem.Replace(CarriageReturnLineFeed, LineFeed, StringComparison.Ordinal).Split(LineFeed, StringSplitOptions.RemoveEmptyEntries);
        return string.Join(LineFeed, lines[0], string.Concat(lines[1..^1]), lines[^1]);
    }

    private static string FixtureMaterial(string keyId) => FixtureKeySet.Default.SymmetricKeys.Single(key => key.KeyId == keyId).Material;

    private static List<string> HarvestDigests(string[]? kinds)
    {
        using var document = JsonDocument.Parse(File.ReadAllText(PublishedMaterialTests.RepositoryFile(HarvestDigestFile)));
        return document.RootElement.GetProperty("digests").EnumerateArray()
            .Where(entry => kinds is null || kinds.Contains(entry.GetProperty("kind").GetString(), StringComparer.Ordinal))
            .Select(entry => entry.GetProperty("sha256").GetString()!)
            .ToList();
    }
}

/// <summary>Host configurations that carry one fixture key in one place, and nothing else wrong.</summary>
internal static class FixtureBoot
{
    private const string FixtureKeyId = "fx";
    private const string Audience = "Starter.Api";
    private const string ServiceName = "starter-worker";
    private const string HttpClientName = "fixture-caller";

    /// <summary>Production settings with <paramref name="material"/> as the only key of <paramref name="section"/>'s ring.</summary>
    public static TestHostOptions Options(string section, string material, string environment = "Production")
    {
        var options = SecurityTestHost.BaseOptions(environment);
        var settings = options.Settings;
        TestSettings.AddMaterial(settings, section, FixtureKeyId, material);
        settings[$"{SecuritySections.RequestSigning}:Mode"] = section == SecuritySections.RequestSigning ? nameof(SecurityEnforcementMode.Enforce) : nameof(SecurityEnforcementMode.Off);
        settings[$"{SecuritySections.RequestSigning}:Audience"] = Audience;
        settings[$"{SecuritySections.ReplayProtection}:AcknowledgeSingleInstance"] = bool.TrueString;
        settings[$"{SecuritySections.Tickets}:Enabled"] = (section == SecuritySections.Tickets).ToString();
        settings[$"{SecuritySections.Uploads}:ReceiptsEnabled"] = (section == SecuritySections.Uploads).ToString();
        return options;
    }

    /// <summary>Production settings with a service-to-service server trusting <paramref name="publicKeyPem"/>.</summary>
    public static TestHostOptions ServiceAssertionServer(string publicKeyPem)
    {
        var options = SecurityTestHost.BaseOptions();
        var section = SecuritySections.ServiceAssertionServer;
        options.Settings[$"{section}:{nameof(ServiceAssertionServerOptions.Audience)}"] = Audience;
        options.Settings[$"{section}:TrustedServices:0:{nameof(TrustedServiceOptions.ServiceName)}"] = ServiceName;
        options.Settings[$"{section}:TrustedServices:0:{nameof(TrustedServiceOptions.KeyId)}"] = FixtureKeyId;
        options.Settings[$"{section}:TrustedServices:0:{nameof(TrustedServiceOptions.PublicKeyPem)}"] = publicKeyPem;
        options.Settings[$"{SecuritySections.ReplayProtection}:AcknowledgeSingleInstance"] = bool.TrueString;
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(options.Settings).Build();
        options.ConfigureServices = services => services.AddAuthentication().AddCodelabServiceAssertion(configuration);
        return options;
    }

    /// <summary>Production settings with a service-to-service client signing with <paramref name="privateKeyPem"/>.</summary>
    public static TestHostOptions ServiceAssertionClient(string privateKeyPem)
    {
        var options = SecurityTestHost.BaseOptions();
        var section = SecuritySections.ServiceAssertionClient;
        options.Settings[$"{section}:{nameof(ServiceAssertionClientOptions.ServiceName)}"] = ServiceName;
        options.Settings[$"{section}:{nameof(ServiceAssertionClientOptions.Audience)}"] = Audience;
        options.Settings[$"{section}:{nameof(ServiceAssertionClientOptions.KeyId)}"] = FixtureKeyId;
        options.Settings[$"{section}:{nameof(ServiceAssertionClientOptions.PrivateKeyPem)}"] = privateKeyPem;
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(options.Settings).Build();
        options.ConfigureServices = services => services.AddHttpClient(HttpClientName).AddCodelabServiceAssertion(configuration);
        return options;
    }
}
