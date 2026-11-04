using System.Text.Json;

namespace Package.Sample.Tests.Support;

/// <summary>
/// The starter's TEST-ONLY fixture keys (packages/vectors/fixture-keys). They are on the published-material denylist
/// inside Codelab.Security, so a Production boot that configures one must refuse to start.
/// </summary>
internal static class FixtureKeys
{
    public const string SigningFile = "signing.json";
    public const string PublicIdFile = "public-id.json";
    public const string ServiceAssertionFile = "service-assertion.json";
    private const string FixtureFolder = "packages/vectors/fixture-keys";

    /// <summary>The active key of a fixture ring file: (key id, base64 material).</summary>
    public static (string KeyId, string Material) ActiveRingKey(string fileName)
    {
        using var document = JsonDocument.Parse(File.ReadAllText(Path.Combine(Folder(), fileName)));
        var ring = document.RootElement.GetProperty("ring");
        var activeKeyId = ring.GetProperty("activeKeyId").GetString()!;
        var active = ring.GetProperty("keys").EnumerateArray().Single(key => key.GetProperty("keyId").GetString() == activeKeyId);
        return (activeKeyId, active.GetProperty("materialBase64").GetString()!);
    }

    /// <summary>The public key PEM of the service-assertion fixture pair (its private half is committed beside it).</summary>
    public static string ServicePublicKeyPem()
    {
        using var document = JsonDocument.Parse(File.ReadAllText(Path.Combine(Folder(), ServiceAssertionFile)));
        return document.RootElement.GetProperty("publicKeyPem").GetString()!;
    }

    /// <summary>The fixture folder, found by walking up from the test output to the repository.</summary>
    public static string Folder()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            var candidate = Path.Combine(directory.FullName, FixtureFolder);
            if (Directory.Exists(candidate))
            {
                return candidate;
            }
        }

        throw new DirectoryNotFoundException($"{FixtureFolder} was not found above {AppContext.BaseDirectory}.");
    }
}
