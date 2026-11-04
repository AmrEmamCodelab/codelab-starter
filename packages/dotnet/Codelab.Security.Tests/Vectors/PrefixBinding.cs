using System.Text;
using Codelab.Testing.Vectors;

namespace Codelab.Security.Tests.Vectors;

/// <summary>
/// What a second protocol prefix must change and what it must not. Canonical bytes carry the new prefix (with the
/// right length prefixes); MACs, headers, tokens and session keys differ from the pinned set; verdicts, outcomes, ids
/// and body hashes are the same, because the prefix binds bytes, it does not change behaviour.
/// </summary>
internal static class PrefixBinding
{
    private static readonly string[] PrefixBoundMarkers = ["Canonical", "Signature", "Header", "Token", "SessionKey"];

    private const string CanonicalMarker = "Canonical";

    /// <summary>Asserts the rules above for every case <paramref name="pinned"/> and <paramref name="generated"/> share by name.</summary>
    public static void AssertBound(GoldenVectorFile pinned, GoldenVectorFile generated)
    {
        var pinnedCases = pinned.Cases.ToDictionary(vectorCase => vectorCase.Name, StringComparer.Ordinal);
        Assert.Equal(pinnedCases.Keys.Order(StringComparer.Ordinal), generated.Cases.Select(vectorCase => vectorCase.Name).Order(StringComparer.Ordinal));
        foreach (var vectorCase in generated.Cases)
        {
            foreach (var (field, value) in vectorCase.Expected)
            {
                AssertField(field, pinnedCases[vectorCase.Name].Expected[field], value, generated.Prefix);
            }
        }
    }

    private static void AssertField(string field, string pinnedValue, string generatedValue, string prefix)
    {
        if (!PrefixBoundMarkers.Any(marker => field.Contains(marker, StringComparison.Ordinal)))
        {
            Assert.Equal(pinnedValue, generatedValue);
            return;
        }

        Assert.NotEqual(pinnedValue, generatedValue);
        if (field.Contains(CanonicalMarker, StringComparison.Ordinal))
        {
            Assert.Contains(prefix, Encoding.UTF8.GetString(Convert.FromBase64String(generatedValue)), StringComparison.Ordinal);
        }
    }
}
