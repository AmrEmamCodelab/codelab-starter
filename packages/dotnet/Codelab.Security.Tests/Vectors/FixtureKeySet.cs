using System.Text.Json;
using Codelab.Security.Keys;

namespace Codelab.Security.Tests.Vectors;

/// <summary>A fixture key ring: the active key id and every key's base64 material.</summary>
internal sealed record FixtureRing(string ActiveKeyId, IReadOnlyDictionary<string, string> Materials)
{
    /// <summary>The raw bytes of one key.</summary>
    public byte[] Bytes(string keyId) => Convert.FromBase64String(Materials[keyId]);

    /// <summary>The ring as options, optionally with another key active (to mint under a retired key).</summary>
    public KeyRingOptions ToOptions(string? activeKeyId = null)
    {
        var options = new KeyRingOptions { ActiveKeyId = activeKeyId ?? ActiveKeyId };
        foreach (var (keyId, material) in Materials)
        {
            options.Keys.Add(new KeyEntryOptions { KeyId = keyId, MaterialBase64 = material });
        }

        return options;
    }
}

/// <summary>The ES256 fixture key pair of the service-to-service scheme.</summary>
internal sealed record ServiceAssertionFixture(string KeyId, string PrivateKeyPem, string PublicKeyPem);

/// <summary>
/// The starter's TEST-ONLY fixture keys (<c>packages/vectors/fixture-keys</c>, written by
/// <c>node tools/vectors/fixture-keys.mjs generate</c>). They pin the golden vectors and sit on the published-material
/// denylist; a file without the TEST-ONLY label is refused.
/// </summary>
internal sealed class FixtureKeySet
{
    private const string SigningFile = "signing.json";
    private const string PublicIdFile = "public-id.json";
    private const string ServiceAssertionFile = "service-assertion.json";
    private const string TestOnlyField = "testOnly";
    private const string RingField = "ring";
    private const string ActiveKeyIdField = "activeKeyId";
    private const string KeysField = "keys";
    private const string KeyIdField = "keyId";
    private const string MaterialField = "materialBase64";
    private const string PrivatePemField = "privateKeyPem";
    private const string PublicPemField = "publicKeyPem";

    private static readonly Lazy<FixtureKeySet> CheckedIn = new(() => Load(VectorEnvironment.FixtureKeysDirectory));

    private FixtureKeySet(FixtureRing signing, FixtureRing publicIds, ServiceAssertionFixture serviceAssertion)
    {
        Signing = signing;
        PublicIds = publicIds;
        ServiceAssertion = serviceAssertion;
    }

    /// <summary>The checked-in set.</summary>
    public static FixtureKeySet Default => CheckedIn.Value;

    /// <summary>The request-signing master key ring.</summary>
    public FixtureRing Signing { get; }

    /// <summary>The PublicId key ring.</summary>
    public FixtureRing PublicIds { get; }

    /// <summary>The ES256 key pair.</summary>
    public ServiceAssertionFixture ServiceAssertion { get; }

    /// <summary>Every symmetric key value of the set (both rings).</summary>
    public IEnumerable<(string KeyId, string Material)> SymmetricKeys =>
        Signing.Materials.Concat(PublicIds.Materials).Select(entry => (entry.Key, entry.Value));

    /// <summary>Reads and checks the fixture files in <paramref name="directory"/>.</summary>
    public static FixtureKeySet Load(string directory)
    {
        using var signing = Open(directory, SigningFile);
        using var publicIds = Open(directory, PublicIdFile);
        using var serviceAssertion = Open(directory, ServiceAssertionFile);
        var pair = serviceAssertion.RootElement;
        return new FixtureKeySet(
            ReadRing(signing.RootElement),
            ReadRing(publicIds.RootElement),
            new ServiceAssertionFixture(
                pair.GetProperty(KeyIdField).GetString()!,
                pair.GetProperty(PrivatePemField).GetString()!,
                pair.GetProperty(PublicPemField).GetString()!));
    }

    private static JsonDocument Open(string directory, string file)
    {
        var path = Path.Combine(directory, file);
        var document = JsonDocument.Parse(File.ReadAllText(path));
        if (!document.RootElement.TryGetProperty(TestOnlyField, out var testOnly) || testOnly.ValueKind != JsonValueKind.True)
        {
            document.Dispose();
            throw new InvalidDataException($"{path} is not labelled TEST-ONLY ({TestOnlyField}: true).");
        }

        return document;
    }

    private static FixtureRing ReadRing(JsonElement root)
    {
        var ring = root.GetProperty(RingField);
        var materials = ring.GetProperty(KeysField).EnumerateArray().ToDictionary(
            key => key.GetProperty(KeyIdField).GetString()!,
            key => key.GetProperty(MaterialField).GetString()!,
            StringComparer.Ordinal);
        return new FixtureRing(ring.GetProperty(ActiveKeyIdField).GetString()!, materials);
    }
}
