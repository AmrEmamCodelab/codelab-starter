using System.Buffers.Text;

namespace Codelab.Security.Canonical;

/// <summary>
/// The one place the package spells unpadded base64url (<c>A-Z a-z 0-9 - _</c>, no padding, no whitespace).
/// <para>
/// The decode is hand-written and total: the BCL decoder throws on some attacker-chosen final quanta, and a verifier
/// that throws turns a forged MAC into an unhandled exception. Non-zero trailing bits are refused, so one byte string
/// has exactly one spelling (a MAC with several spellings could be walked past the replay store).
/// </para>
/// </summary>
public static class Base64UrlText
{
    private const int BitsPerByte = 8;
    private const int BitsPerCharacter = 6;
    private const int RoundUp = BitsPerCharacter - 1;
    private const int LowerCaseOffset = 26;
    private const int DigitOffset = 52;
    private const int DashValue = 62;
    private const int UnderscoreValue = 63;
    private const int NotInAlphabet = -1;

    /// <summary>Unpadded base64url characters needed to carry <paramref name="byteCount"/> bytes.</summary>
    public static int CharacterCount(int byteCount) => ((byteCount * BitsPerByte) + RoundUp) / BitsPerCharacter;

    /// <summary>True when <paramref name="value"/> is exactly <paramref name="expectedLength"/> base64url characters.</summary>
    public static bool IsUnpadded(ReadOnlySpan<char> value, int expectedLength) =>
        value.Length == expectedLength && IsAlphabetOnly(value);

    /// <summary>True when every character of <paramref name="value"/> is in the unpadded base64url alphabet.</summary>
    public static bool IsAlphabetOnly(ReadOnlySpan<char> value)
    {
        foreach (var c in value)
        {
            if (SextetOf(c) == NotInAlphabet)
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>Encodes to unpadded base64url.</summary>
    public static string Encode(ReadOnlySpan<byte> bytes) => Base64Url.EncodeToString(bytes);

    /// <summary>Decodes <paramref name="value"/> into a new array; false (never throws) for anything malformed.</summary>
    public static bool TryDecode(ReadOnlySpan<char> value, out byte[] bytes)
    {
        var decoded = new byte[value.Length * BitsPerCharacter / BitsPerByte];
        var ok = TryDecode(value, decoded.AsSpan());
        bytes = ok ? decoded : [];
        return ok;
    }

    /// <summary>
    /// Decodes <paramref name="value"/> into <paramref name="destination"/>, which must be exactly the decoded size.
    /// Returns false (never throws) for wrong length, out-of-alphabet characters or non-zero trailing bits.
    /// </summary>
    public static bool TryDecode(ReadOnlySpan<char> value, Span<byte> destination)
    {
        if (!IsUnpadded(value, CharacterCount(destination.Length)))
        {
            return false;
        }

        var accumulator = 0;
        var bitsHeld = 0;
        var written = 0;
        foreach (var c in value)
        {
            accumulator = (accumulator << BitsPerCharacter) | SextetOf(c);
            bitsHeld += BitsPerCharacter;
            if (bitsHeld < BitsPerByte)
            {
                continue;
            }

            bitsHeld -= BitsPerByte;
            destination[written++] = (byte)(accumulator >> bitsHeld);
            accumulator &= (1 << bitsHeld) - 1;
        }

        return written == destination.Length && accumulator == 0;
    }

    private static int SextetOf(char c) => c switch
    {
        >= 'A' and <= 'Z' => c - 'A',
        >= 'a' and <= 'z' => c - 'a' + LowerCaseOffset,
        >= '0' and <= '9' => c - '0' + DigitOffset,
        '-' => DashValue,
        '_' => UnderscoreValue,
        _ => NotInAlphabet,
    };
}
