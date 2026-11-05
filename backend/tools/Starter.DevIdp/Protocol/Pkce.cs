using System.Security.Cryptography;
using System.Text;
using Microsoft.IdentityModel.Tokens;

namespace Starter.DevIdp.Protocol;

/// <summary>
/// PKCE (RFC 7636) with the S256 method only: a verifier or challenge is 43 to 128 unreserved characters
/// (<c>A-Z a-z 0-9 - . _ ~</c>), and the challenge is BASE64URL(SHA-256(ASCII(verifier))).
/// </summary>
internal static class Pkce
{
    /// <summary>The shortest verifier or challenge RFC 7636 allows.</summary>
    public const int MinLength = 43;

    /// <summary>The longest verifier or challenge RFC 7636 allows.</summary>
    public const int MaxLength = 128;

    private const string UnreservedPunctuation = "-._~";

    /// <summary>Whether <paramref name="value"/> has the length and character set of a verifier or challenge.</summary>
    public static bool IsWellFormed(string? value) =>
        value is { Length: >= MinLength and <= MaxLength } && value.All(IsUnreserved);

    /// <summary>The S256 challenge of <paramref name="verifier"/>.</summary>
    public static string ChallengeOf(string verifier) =>
        Base64UrlEncoder.Encode(SHA256.HashData(Encoding.ASCII.GetBytes(verifier)));

    /// <summary>Whether <paramref name="verifier"/> is well formed and its S256 challenge equals <paramref name="challenge"/> (fixed time).</summary>
    public static bool Verify(string? verifier, string challenge)
    {
        if (!IsWellFormed(verifier))
        {
            return false;
        }

        return CryptographicOperations.FixedTimeEquals(
            Encoding.ASCII.GetBytes(ChallengeOf(verifier!)),
            Encoding.ASCII.GetBytes(challenge));
    }

    private static bool IsUnreserved(char character) =>
        char.IsAsciiLetterOrDigit(character) || UnreservedPunctuation.Contains(character, StringComparison.Ordinal);
}
