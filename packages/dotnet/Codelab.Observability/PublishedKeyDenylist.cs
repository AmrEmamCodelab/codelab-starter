using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Codelab.Observability;

/// <summary>
/// Key material that is published and therefore worthless as a key: the harvest source's committed keys and the
/// starter's own test fixture keys. The data files are Codelab.Security's <c>PublishedMaterial/*.json</c>, linked into
/// this assembly at build time (one source of truth, no package dependency). Each entry is the SHA-256 of a key digest's
/// bytes (see Codelab.Security <c>PublishedMaterialDenylist</c>); only digests are stored, never values.
/// </summary>
internal sealed class PublishedKeyDenylist
{
    /// <summary>The resource-name prefix of the linked digest files.</summary>
    internal const string ResourcePrefix = "Codelab.Observability.PublishedMaterial.";

    private const string EntriesProperty = "keyDigestHashes";
    private const string ResourceExtension = ".json";

    private static readonly Lazy<PublishedKeyDenylist> EmbeddedList = new(LoadEmbedded);

    private readonly HashSet<string> _entries;

    private PublishedKeyDenylist(HashSet<string> entries) => _entries = entries;

    /// <summary>The list linked into the package.</summary>
    internal static PublishedKeyDenylist Embedded => EmbeddedList.Value;

    /// <summary>The number of distinct entries.</summary>
    internal int Count => _entries.Count;

    /// <summary>A list built from explicit key digests (lower- or upper-case hex SHA-256 of a key value or its bytes).</summary>
    internal static PublishedKeyDenylist FromKeyDigests(IEnumerable<string> keyDigests) =>
        new(new HashSet<string>(keyDigests.Select(EntryFor), StringComparer.Ordinal));

    /// <summary>True when the key digest (hex SHA-256 of a key as written, or of its decoded bytes) is on the list.</summary>
    internal bool ContainsKeyDigest(string keyDigestHex) => _entries.Contains(EntryFor(keyDigestHex));

    /// <summary>True when the key, as written or as its decoded bytes, is on the list.</summary>
    internal bool IsPublished(string keyText, byte[] keyBytes) =>
        ContainsKeyDigest(Sha256Hex(Encoding.UTF8.GetBytes(keyText)))
        || ContainsKeyDigest(Sha256Hex(Encoding.UTF8.GetBytes(keyText.Trim())))
        || ContainsKeyDigest(Sha256Hex(keyBytes));

    private static string Sha256Hex(byte[] bytes) => Convert.ToHexStringLower(SHA256.HashData(bytes));

    private static string EntryFor(string keyDigestHex) => Sha256Hex(Convert.FromHexString(keyDigestHex.Trim()));

    private static PublishedKeyDenylist LoadEmbedded()
    {
        var assembly = typeof(PublishedKeyDenylist).Assembly;
        var entries = new HashSet<string>(StringComparer.Ordinal);
        foreach (var name in assembly.GetManifestResourceNames().Where(IsDenylistResource))
        {
            entries.UnionWith(ReadEntries(assembly, name));
        }

        return new PublishedKeyDenylist(entries);
    }

    private static bool IsDenylistResource(string name) =>
        name.StartsWith(ResourcePrefix, StringComparison.Ordinal) && name.EndsWith(ResourceExtension, StringComparison.Ordinal);

    private static List<string> ReadEntries(Assembly assembly, string resourceName)
    {
        using var stream = assembly.GetManifestResourceStream(resourceName)
            ?? throw new InvalidOperationException($"The embedded denylist resource '{resourceName}' could not be opened.");
        using var document = JsonDocument.Parse(stream);
        if (!document.RootElement.TryGetProperty(EntriesProperty, out var entries) || entries.ValueKind != JsonValueKind.Array)
        {
            throw new InvalidOperationException($"The embedded denylist resource '{resourceName}' has no '{EntriesProperty}' array.");
        }

        return entries.EnumerateArray().Select(entry => (entry.GetString() ?? string.Empty).Trim().ToLowerInvariant()).ToList();
    }
}
