using System.Security.Cryptography;
using Codelab.Security.Canonical;

namespace Codelab.Security.Signing;

/// <summary>
/// The MAC of <c>{Prefix}-SIG-v1</c>: HMAC-SHA256 over a canonical form, published as 43 characters of unpadded
/// base64url. A valid MAC proves the sender held the session key: integrity, anti-replay and anti-CSRF. It does not
/// identify the sender (the server derives the same key), so it is not authentication or non-repudiation.
/// The key is always the raw key bytes (base64-decoded), never the UTF-8 text of a base64 string.
/// </summary>
public static class HmacSignature
{
    /// <summary>An HMAC-SHA256 tag is 32 bytes.</summary>
    public const int MacBytes = 32;

    /// <summary>The tag as unpadded base64url is 43 characters.</summary>
    public const int MacCharacters = 43;

    /// <summary>The MAC over <paramref name="canonical"/> as it appears on the wire.</summary>
    /// <param name="key">The key as raw bytes.</param>
    /// <param name="canonical">The UTF-8 bytes of a canonical form (never a hand-assembled string).</param>
    public static string Compute(ReadOnlySpan<byte> key, ReadOnlySpan<byte> canonical)
    {
        Span<byte> mac = stackalloc byte[MacBytes];
        HMACSHA256.HashData(key, canonical, mac);
        return Base64UrlText.Encode(mac);
    }

    /// <summary>
    /// Verifies a presented MAC in constant time. Null, wrong length and out-of-alphabet input return false; nothing
    /// throws, because every value reaching <paramref name="presentedMac"/> was chosen by somebody else.
    /// </summary>
    public static bool Verify(ReadOnlySpan<byte> key, ReadOnlySpan<byte> canonical, string? presentedMac)
    {
        if (presentedMac is null)
        {
            return false;
        }

        Span<byte> presented = stackalloc byte[MacBytes];
        if (!Base64UrlText.TryDecode(presentedMac, presented))
        {
            return false;
        }

        Span<byte> expected = stackalloc byte[MacBytes];
        HMACSHA256.HashData(key, canonical, expected);
        return CryptographicOperations.FixedTimeEquals(expected, presented);
    }
}
