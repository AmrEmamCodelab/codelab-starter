using System.Security.Cryptography;
using System.Text;

namespace Codelab.Security.Keys;

/// <summary>
/// The digests that identify a key value, computed exactly like <c>tools/extract/lib/key-material.mjs</c>: the
/// SHA-256 (lower-case hex) of the value as written (UTF-8), plus the SHA-256 of each byte sequence the value decodes
/// to (hex, base64 or base64url). A key re-encoded from base64 to hex or base64url is therefore still recognised.
/// <para>
/// An asymmetric key adds one more digest: the SHA-256 of its DER <c>SubjectPublicKeyInfo</c>
/// (<see cref="AsymmetricKeyIdentity"/>), read from PEM text or from DER bytes the value decodes to. Every spelling of a
/// PEM key (one-line body, extra whitespace, PKCS#8 or SEC1) and both halves of a key pair share that digest. The
/// extract scanner writes it too: the base64 body of a public-key PEM decodes to exactly those bytes.
/// </para>
/// </summary>
public static class KeyMaterialDigests
{
    private const int MinHexKeyCharacters = 32;
    private const int MinBase64KeyCharacters = 22;
    private const int HexDigitsPerByte = 2;
    private const int Base64Quantum = 4;
    private const int MaxPaddingCharacters = 2;
    private const char Padding = '=';

    /// <summary>Every digest that identifies <paramref name="value"/> (text first, then each decoded reading).</summary>
    public static IReadOnlyList<string> Of(string value)
    {
        ArgumentNullException.ThrowIfNull(value);

        var digests = new List<string> { Sha256Hex(Encoding.UTF8.GetBytes(value)) };
        var readings = DecodedReadings(value).ToList();
        foreach (var bytes in readings)
        {
            digests.Add(Sha256Hex(bytes));
        }

        var identity = AsymmetricKeyIdentity.FromPem(value) ?? readings.Select(AsymmetricKeyIdentity.FromDer).FirstOrDefault(info => info is not null);
        if (identity is not null && !digests.Contains(Sha256Hex(identity)))
        {
            digests.Add(Sha256Hex(identity));
        }

        return digests;
    }

    /// <summary>The lower-case hex SHA-256 of <paramref name="bytes"/>.</summary>
    public static string Sha256Hex(ReadOnlySpan<byte> bytes) => Convert.ToHexStringLower(SHA256.HashData(bytes));

    private static IEnumerable<byte[]> DecodedReadings(string value)
    {
        if (TryDecodeHex(value, out var hex))
        {
            yield return hex;
        }

        if (TryDecodeBase64(value, out var base64))
        {
            yield return base64;
        }
    }

    private static bool TryDecodeHex(string value, out byte[] bytes)
    {
        bytes = [];
        if (value.Length < MinHexKeyCharacters || value.Length % HexDigitsPerByte != 0 || !value.All(char.IsAsciiHexDigit))
        {
            return false;
        }

        bytes = Convert.FromHexString(value);
        return true;
    }

    private static bool TryDecodeBase64(string value, out byte[] bytes)
    {
        bytes = [];
        if (value.Length < MinBase64KeyCharacters)
        {
            return false;
        }

        string standard;
        if (IsStandardBase64(value))
        {
            standard = value.TrimEnd(Padding);
        }
        else if (Canonical.Base64UrlText.IsAlphabetOnly(value))
        {
            standard = value.Replace('-', '+').Replace('_', '/');
        }
        else
        {
            return false;
        }

        // Decoders are lenient about trailing bits; a reading only counts when it round-trips (as in key-material.mjs).
        bytes = DecodeStandard(standard);
        return string.Equals(Convert.ToBase64String(bytes).TrimEnd(Padding), standard, StringComparison.Ordinal);
    }

    private static bool IsStandardBase64(string value)
    {
        var body = value.TrimEnd(Padding);
        var paddingLength = value.Length - body.Length;
        return paddingLength <= MaxPaddingCharacters && body.Length > 0 && body.All(IsStandardBase64Character);
    }

    private static bool IsStandardBase64Character(char c) => char.IsAsciiLetterOrDigit(c) || c is '+' or '/';

    private static byte[] DecodeStandard(string unpadded)
    {
        var remainder = unpadded.Length % Base64Quantum;
        var padded = remainder == 0 ? unpadded : unpadded + new string(Padding, Base64Quantum - remainder);
        var buffer = new byte[padded.Length];
        return Convert.TryFromBase64String(padded, buffer, out var written) ? buffer[..written] : DecodeLenient(unpadded);
    }

    // A length that is 1 mod 4 cannot be base64; Node drops the dangling character, so the reading never round-trips.
    private static byte[] DecodeLenient(string unpadded) => DecodeStandard(unpadded[..^1]);
}
