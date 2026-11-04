using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Codelab.Security.Keys;
using Codelab.Security.Tests.TestSupport;

namespace Codelab.Security.Tests.Keys;

public sealed class PublishedMaterialTests
{
    private static readonly string[] KeyMaterialKinds = ["config-secret", "script-secret", "test-fixture", "vector-key"];

    [Fact]
    public void The_embedded_denylist_loads_every_published_material_resource()
    {
        var denylist = PublishedMaterialDenylist.Embedded;

        Assert.Contains(PublishedMaterialDenylist.ResourcePrefix + "published-material.source.json", denylist.Sources);
        Assert.All(denylist.Sources, source => Assert.EndsWith(".json", source, StringComparison.Ordinal));
        Assert.True(denylist.Count >= ExpectedSourceDigests().Count);
    }

    [Fact]
    public void Every_harvest_source_key_material_digest_is_on_the_embedded_denylist()
    {
        var missing = ExpectedSourceDigests().Where(digest => !PublishedMaterialDenylist.Embedded.ContainsDigest(digest)).ToList();

        Assert.True(missing.Count == 0, $"published-material.source.json is stale: {missing.Count} key-material digest(s) of tools/extract/secret-digests.json (kinds {string.Join(", ", KeyMaterialKinds)}) are missing. Regenerate it from that file.");
    }

    [Fact]
    public void The_source_file_holds_digests_only()
    {
        using var document = JsonDocument.Parse(File.ReadAllText(RepositoryFile("packages/dotnet/Codelab.Security/PublishedMaterial/published-material.source.json")));

        Assert.All(document.RootElement.GetProperty(PublishedMaterialDenylist.EntriesProperty).EnumerateArray(), digest => Assert.Matches("^[0-9a-f]{64}$", digest.GetString()));
    }

    [Fact]
    public void A_key_is_recognised_as_written_and_in_every_re_encoding()
    {
        var bytes = RandomNumberGenerator.GetBytes(TestKeys.KeyBytes);
        var base64 = Convert.ToBase64String(bytes);
        var denylist = PublishedMaterialDenylist.FromDigests([KeyMaterialDigests.Sha256Hex(bytes)]);

        Assert.True(denylist.IsPublished(base64));
        Assert.True(denylist.IsPublished(base64.TrimEnd('=')));
        Assert.True(denylist.IsPublished(Convert.ToHexStringLower(bytes)));
        Assert.True(denylist.IsPublished(Convert.ToHexString(bytes)));
        Assert.True(denylist.IsPublished(Base64Url(bytes)));
        Assert.False(denylist.IsPublished(TestKeys.NewKey()));
    }

    [Fact]
    public void The_text_digest_is_the_sha256_of_the_utf8_text_like_the_extract_scanner()
    {
        const string value = "a config value with spaces, not a key";

        Assert.Equal([Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(value)))], KeyMaterialDigests.Of(value));
    }

    [Fact]
    public void The_guard_refuses_a_published_key_and_names_the_setting_never_the_value()
    {
        var ring = TestKeys.Ring("k1");
        var material = ring.Keys[0].MaterialBase64!;
        var guard = new KeyRingGuard(PublishedMaterialDenylist.FromDigests(KeyMaterialDigests.Of(material)), checkPublished: true);

        var result = guard.Check(ring, "Codelab:Security:PublicIds");

        Assert.False(result.IsValid);
        Assert.Contains("Codelab:Security:PublicIds:Keys[k1]", result.Error, StringComparison.Ordinal);
        Assert.DoesNotContain(material, result.Error, StringComparison.Ordinal);
    }

    [Fact]
    public void The_guard_accepts_a_fresh_key_and_skips_the_list_only_when_told_to()
    {
        var ring = TestKeys.Ring("k1");
        var published = PublishedMaterialDenylist.FromDigests(KeyMaterialDigests.Of(ring.Keys[0].MaterialBase64!));

        Assert.True(new KeyRingGuard(PublishedMaterialDenylist.Embedded, checkPublished: true).Check(ring, "S").IsValid);
        Assert.True(new KeyRingGuard(published, checkPublished: false).Check(ring, "S").IsValid);
    }

    [Fact]
    public void A_file_entry_is_the_sha256_of_the_key_digest_bytes()
    {
        var keyDigest = KeyMaterialDigests.Sha256Hex(RandomNumberGenerator.GetBytes(TestKeys.KeyBytes));

        Assert.Equal(Convert.ToHexStringLower(SHA256.HashData(Convert.FromHexString(keyDigest))), PublishedMaterialDenylist.EntryFor(keyDigest));
        Assert.Equal(PublishedMaterialDenylist.EntryFor(keyDigest), PublishedMaterialDenylist.EntryFor(keyDigest.ToUpperInvariant()));
    }

    [Fact]
    public void A_union_keeps_both_lists()
    {
        var extra = PublishedMaterialDenylist.FromDigests([new string('a', 64)]);

        var union = PublishedMaterialDenylist.Embedded.Union(extra);

        Assert.True(union.ContainsDigest(new string('A', 64)));
        Assert.Equal(PublishedMaterialDenylist.Embedded.Count + 1, union.Count);
    }

    internal static string RepositoryFile(string relativePath)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "codelab-starter.slnx")))
        {
            directory = directory.Parent;
        }

        return Path.Combine(directory?.FullName ?? throw new InvalidOperationException("The repository root (codelab-starter.slnx) was not found."), relativePath);
    }

    private static List<string> ExpectedSourceDigests()
    {
        using var document = JsonDocument.Parse(File.ReadAllText(RepositoryFile("tools/extract/secret-digests.json")));
        return document.RootElement.GetProperty("digests").EnumerateArray()
            .Where(entry => KeyMaterialKinds.Contains(entry.GetProperty("kind").GetString()))
            .Select(entry => entry.GetProperty("sha256").GetString()!)
            .ToList();
    }

    private static string Base64Url(byte[] bytes) => Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
}
