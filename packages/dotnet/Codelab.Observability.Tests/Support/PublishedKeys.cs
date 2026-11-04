using System.Text.Json;

namespace Codelab.Observability.Tests.Support;

/// <summary>Published key material the user-hash guard must refuse, read from the repository (values or digests).</summary>
internal static class PublishedKeys
{
    private const string FixtureKeysFolder = "packages/vectors/fixture-keys";
    private const string HarvestDigestFile = "tools/extract/secret-digests.json";
    private static readonly string[] HarvestKeyKinds = ["config-secret", "script-secret", "test-fixture", "vector-key"];

    /// <summary>Every symmetric key of the starter's TEST-ONLY fixture rings (base64).</summary>
    public static List<string> FixtureRingMaterials()
    {
        var materials = new List<string>();
        foreach (var file in Directory.GetFiles(RepositoryPath(FixtureKeysFolder), "*.json"))
        {
            using var document = JsonDocument.Parse(File.ReadAllText(file));
            if (document.RootElement.TryGetProperty("ring", out var ring))
            {
                materials.AddRange(ring.GetProperty("keys").EnumerateArray().Select(key => key.GetProperty("materialBase64").GetString()!));
            }
        }

        return materials;
    }

    /// <summary>The key-material digests of the harvest source (the extract denylist).</summary>
    public static List<string> HarvestKeyDigests()
    {
        using var document = JsonDocument.Parse(File.ReadAllText(RepositoryPath(HarvestDigestFile)));
        return document.RootElement.GetProperty("digests").EnumerateArray()
            .Where(entry => HarvestKeyKinds.Contains(entry.GetProperty("kind").GetString(), StringComparer.Ordinal))
            .Select(entry => entry.GetProperty("sha256").GetString()!)
            .ToList();
    }

    private static string RepositoryPath(string relativePath)
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            var candidate = Path.Combine(directory.FullName, relativePath);
            if (Path.Exists(candidate))
            {
                return candidate;
            }
        }

        throw new DirectoryNotFoundException($"{relativePath} was not found above {AppContext.BaseDirectory}.");
    }
}
