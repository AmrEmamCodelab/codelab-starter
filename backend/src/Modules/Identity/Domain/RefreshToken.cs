using System.Diagnostics.CodeAnalysis;

namespace Starter.Modules.Identity.Domain;

/// <summary>
/// The refresh token a client holds: <c>&lt;session id&gt;.&lt;secret&gt;</c>. The session id locates the session; the secret
/// proves possession and is compared with the stored hash. Opaque to the client.
/// </summary>
/// <param name="SessionId">The session id (32 lower-case hex characters).</param>
/// <param name="Secret">The secret (base64url).</param>
public sealed record RefreshToken(string SessionId, string Secret)
{
    /// <summary>The separator between the session id and the secret.</summary>
    public const char Separator = '.';

    private const string RedactedSecret = "***";

    /// <summary>The token text the client holds.</summary>
    public string Format() => SessionId + Separator + Secret;

    /// <summary>A log-safe description: the secret is never printed.</summary>
    public override string ToString() => nameof(RefreshToken) + "(" + SessionId + Separator + RedactedSecret + ")";

    /// <summary>Reads a token; false for anything that does not have the token's shape.</summary>
    public static bool TryParse(string? text, [NotNullWhen(true)] out RefreshToken? token)
    {
        token = null;
        if (string.IsNullOrEmpty(text) || text.Length > IdentityLimits.RefreshTokenMaxLength)
        {
            return false;
        }

        var separator = text.IndexOf(Separator, StringComparison.Ordinal);
        if (separator != IdentityLimits.SessionIdLength || separator == text.Length - 1)
        {
            return false;
        }

        var sessionId = text[..separator];
        var secret = text[(separator + 1)..];
        if (!sessionId.All(char.IsAsciiHexDigitLower) || !secret.All(IsBase64UrlCharacter))
        {
            return false;
        }

        token = new RefreshToken(sessionId, secret);
        return true;
    }

    private static bool IsBase64UrlCharacter(char character) =>
        char.IsAsciiLetterOrDigit(character) || character is '-' or '_';
}
