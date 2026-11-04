using System.Reflection;
using System.Text.Json;

namespace Codelab.Security.Keys;

/// <summary>
/// SHA-256 digests of key material that is published and therefore worthless as a key: the harvest source's committed
/// keys (<c>PublishedMaterial/published-material.source.json</c>, generated from <c>tools/extract/secret-digests.json</c>)
/// and the starter's own test fixture keys (<c>published-material.fixtures.json</c>). Every embedded
/// <c>PublishedMaterial/*.json</c> resource is loaded. Only digests are stored, never values.
/// <para>
/// <b>File format.</b> <c>{ "keyDigestHashes": [ ... ] }</c>, where each entry is <see cref="EntryFor"/> of a key
/// digest: the lower-case hex SHA-256 of the 32 bytes of the key's SHA-256 (as computed by
/// <c>tools/extract/lib/key-material.mjs</c> <c>digestsOf</c>, one entry per digest). The digests are hashed once more
/// so the file never holds a value that is itself a harvest-source fixture (some fixtures were digests), and the
/// secret scan stays clean. A generator writing a new file: for each key value <c>v</c> and each <c>d</c> in
/// <c>digestsOf(v)</c>, write <c>sha256(hexDecode(d))</c>.
/// </para>
/// </summary>
public sealed class PublishedMaterialDenylist
{
    /// <summary>The resource-name prefix of the embedded digest files.</summary>
    public const string ResourcePrefix = "Codelab.Security.PublishedMaterial.";

    /// <summary>The JSON array property of a published-material file.</summary>
    public const string EntriesProperty = "keyDigestHashes";

    private const string ResourceExtension = ".json";

    private static readonly Lazy<PublishedMaterialDenylist> EmbeddedList = new(LoadEmbedded);

    private readonly HashSet<string> _digests;

    private PublishedMaterialDenylist(HashSet<string> digests, IReadOnlyList<string> sources)
    {
        _digests = digests;
        Sources = sources;
    }

    /// <summary>The denylist shipped inside the package.</summary>
    public static PublishedMaterialDenylist Embedded => EmbeddedList.Value;

    /// <summary>The resource names the list was loaded from.</summary>
    public IReadOnlyList<string> Sources { get; }

    /// <summary>The number of distinct entries.</summary>
    public int Count => _digests.Count;

    /// <summary>The file entry for a key digest: the lower-case hex SHA-256 of the digest's bytes.</summary>
    /// <exception cref="FormatException">The digest is not hex.</exception>
    public static string EntryFor(string keyDigestHex)
    {
        ArgumentNullException.ThrowIfNull(keyDigestHex);
        return KeyMaterialDigests.Sha256Hex(Convert.FromHexString(keyDigestHex.Trim()));
    }

    /// <summary>A list built from explicit key digests (tests, or a host adding its own retired keys).</summary>
    public static PublishedMaterialDenylist FromDigests(IEnumerable<string> keyDigests)
    {
        ArgumentNullException.ThrowIfNull(keyDigests);
        return new PublishedMaterialDenylist(new HashSet<string>(keyDigests.Select(EntryFor), StringComparer.Ordinal), []);
    }

    /// <summary>This list plus <paramref name="other"/>.</summary>
    public PublishedMaterialDenylist Union(PublishedMaterialDenylist other)
    {
        ArgumentNullException.ThrowIfNull(other);
        var merged = new HashSet<string>(_digests, StringComparer.Ordinal);
        merged.UnionWith(other._digests);
        return new PublishedMaterialDenylist(merged, [.. Sources, .. other.Sources]);
    }

    /// <summary>True when the key digest <paramref name="keyDigestHex"/> (hex SHA-256, any case) is on the list.</summary>
    public bool ContainsDigest(string keyDigestHex) => _digests.Contains(EntryFor(keyDigestHex));

    /// <summary>True when any digest of <paramref name="material"/> (text or decoded readings) is on the list.</summary>
    public bool IsPublished(string material) => KeyMaterialDigests.Of(material).Any(ContainsDigest);

    private static string Normalize(string digest) => digest.Trim().ToLowerInvariant();

    private static PublishedMaterialDenylist LoadEmbedded()
    {
        var assembly = typeof(PublishedMaterialDenylist).Assembly;
        var sources = assembly.GetManifestResourceNames()
            .Where(name => name.StartsWith(ResourcePrefix, StringComparison.Ordinal) && name.EndsWith(ResourceExtension, StringComparison.Ordinal))
            .Order(StringComparer.Ordinal)
            .ToList();

        var digests = new HashSet<string>(StringComparer.Ordinal);
        foreach (var source in sources)
        {
            digests.UnionWith(ReadDigests(assembly, source));
        }

        return new PublishedMaterialDenylist(digests, sources);
    }

    private static List<string> ReadDigests(Assembly assembly, string resourceName)
    {
        using var stream = assembly.GetManifestResourceStream(resourceName)
            ?? throw new InvalidOperationException($"The embedded denylist resource '{resourceName}' could not be opened.");
        using var document = JsonDocument.Parse(stream);
        if (!document.RootElement.TryGetProperty(EntriesProperty, out var digests) || digests.ValueKind != JsonValueKind.Array)
        {
            throw new InvalidOperationException($"The embedded denylist resource '{resourceName}' has no '{EntriesProperty}' array.");
        }

        return digests.EnumerateArray().Select(element => Normalize(element.GetString() ?? string.Empty)).ToList();
    }
}
