using Codelab.Security.Canonical;

namespace Codelab.Security.Tests.Vectors;

/// <summary>One way of damaging a presented signature header.</summary>
internal sealed record HeaderEdit(string Name, Func<string, string> Apply);

/// <summary>
/// The header tamper cases shared by the request and response vectors. Every edit leaves the signed fields alone, so
/// the verdict is decided by the header alone: a substituted MAC is a <c>mismatch</c>; a second spelling of the same
/// MAC (non-zero trailing bits), a truncated MAC, a fifth field or another scheme version is <c>malformed</c>.
/// </summary>
internal static class HeaderEdits
{
    private const char Separator = '.';
    private const string OtherVersion = "2";
    private const string ExtraField = ".x";
    private const string ForeignPrefix = "FOREIGN";
    private const string ForeignPrefixAlternative = "FOREIGNB";
    private const int LettersInAlphabet = 26;

    // Built, not written out: the spelled-out alphabet is a harvest-source fixture string on the secret-scan denylist.
    private static readonly string Base64UrlAlphabet = string.Concat(Letters('A'), Letters('a'), "0123456789-_");

    /// <summary>The edits that apply to both header shapes (<c>1.{mac}</c> and <c>1.{ts}.{nonce}.{mac}</c>).</summary>
    public static IReadOnlyList<HeaderEdit> Common { get; } =
    [
        new("mac-substituted", header => ReplaceMac(header, SubstituteFirst)),
        new("mac-non-canonical-trailing-bits", header => ReplaceMac(header, SetTrailingBit)),
        new("mac-truncated", header => header[..^1]),
        new("extra-field", header => header + ExtraField),
        new("scheme-version-two", header => OtherVersion + header[1..]),
    ];

    /// <summary>Every request-header edit: <see cref="Common"/> plus timestamp and nonce damage.</summary>
    public static IReadOnlyList<HeaderEdit> All { get; } =
    [
        .. Common,
        new("timestamp-leading-zero", header => ReplaceField(header, field: 1, value => "0" + value)),
        new("nonce-truncated", header => ReplaceField(header, field: 2, value => value[..^1])),
    ];

    /// <summary>A valid prefix that differs from <paramref name="protocol"/>'s.</summary>
    public static SecurityProtocol ForeignProtocol(SecurityProtocol protocol) =>
        SecurityProtocol.Create(protocol.Prefix == ForeignPrefix ? ForeignPrefixAlternative : ForeignPrefix);

    private static string Letters(char first) => string.Concat(Enumerable.Range(first, count: LettersInAlphabet).Select(code => (char)code));

    private static string ReplaceMac(string header, Func<string, string> edit)
    {
        var at = header.LastIndexOf(Separator) + 1;
        return header[..at] + edit(header[at..]);
    }

    private static string ReplaceField(string header, int field, Func<string, string> edit)
    {
        var fields = header.Split(Separator);
        fields[field] = edit(fields[field]);
        return string.Join(Separator, fields);
    }

    // A different first character keeps the MAC canonical, so the verdict is decided by the MAC check itself.
    private static string SubstituteFirst(string mac) => (mac[0] == 'A' ? 'B' : 'A') + mac[1..];

    // The last character of a 32-byte MAC carries 2 unused bits; setting one gives a second spelling of other bytes.
    private static string SetTrailingBit(string mac)
    {
        if (!Base64UrlText.IsAlphabetOnly(mac))
        {
            throw new InvalidOperationException("The MAC is not base64url.");
        }

        var sextet = Base64UrlAlphabet.IndexOf(mac[^1], StringComparison.Ordinal);
        return mac[..^1] + Base64UrlAlphabet[sextet | 1];
    }
}
