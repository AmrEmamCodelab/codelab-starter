namespace Codelab.Security.PublicIds;

/// <summary>
/// The token grammar of <c>{Prefix}-PUBLICID-v1</c>: <c>m1_{keyId}_{payload}</c>, where the key id is 1-8 of
/// <c>[a-z0-9]</c> and the payload is exactly 48 unpadded base64url characters (12-byte nonce, 16-byte tag, 8-byte
/// ciphertext). Checked by length before anything is scanned, so a megabyte of junk costs a comparison.
/// </summary>
public static class PublicIdFormat
{
    /// <summary>The token version marker.</summary>
    public const string Version = "m1";

    /// <summary>The separator between version, key id and payload.</summary>
    public const char Separator = '_';

    /// <summary>The token prefix (<c>m1_</c>).</summary>
    public const string TokenPrefix = "m1_";

    /// <summary>AES-GCM nonce bytes (derived, synthetic-IV style).</summary>
    public const int NonceBytes = 12;

    /// <summary>AES-GCM tag bytes.</summary>
    public const int TagBytes = 16;

    /// <summary>Plaintext bytes (a big-endian 64-bit id).</summary>
    public const int PlaintextBytes = 8;

    /// <summary>Total payload bytes.</summary>
    public const int PayloadBytes = NonceBytes + TagBytes + PlaintextBytes;

    /// <summary>Payload length as unpadded base64url.</summary>
    public const int PayloadCharacters = 48;

    /// <summary>Shortest key id.</summary>
    public const int MinKeyIdLength = 1;

    /// <summary>Longest key id.</summary>
    public const int MaxKeyIdLength = 8;

    /// <summary>Longest domain label.</summary>
    public const int MaxDomainLength = 32;

    /// <summary>The smallest encodable identifier.</summary>
    public const long MinimumId = 1;

    /// <summary>The one message every refusal carries: it names no reason and never echoes the token.</summary>
    public const string UnreadableMessage = "The value is not a valid identifier.";

    /// <summary>The OpenAPI pattern of a token.</summary>
    public const string OpenApiPattern = "^m1_[a-z0-9]{1,8}_[A-Za-z0-9_-]{48}$";

    private const int ShortestToken = 3 + MinKeyIdLength + 1 + PayloadCharacters;
    private const int LongestToken = 3 + MaxKeyIdLength + 1 + PayloadCharacters;

    /// <summary>True for a key id of 1-8 characters of <c>[a-z0-9]</c>.</summary>
    public static bool IsWellFormedKeyId(string? keyId) =>
        keyId is { Length: >= MinKeyIdLength and <= MaxKeyIdLength } && keyId.All(IsLowerAlphanumeric);

    /// <summary>True for a domain label matching <c>[a-z][a-z0-9-]{0,31}</c>.</summary>
    public static bool IsWellFormedDomain(string? domain) =>
        domain is { Length: > 0 and <= MaxDomainLength }
        && char.IsAsciiLetterLower(domain[0])
        && domain.All(c => IsLowerAlphanumeric(c) || c == '-');

    /// <summary>True when <paramref name="token"/> has the token shape (it may still authenticate under no key).</summary>
    public static bool IsWellFormedToken(string? token) => TrySplit(token, out _, out _);

    /// <summary>Splits a well-formed token into its key id and payload.</summary>
    public static bool TrySplit(string? token, out string keyId, out string payload)
    {
        keyId = string.Empty;
        payload = string.Empty;
        if (token is null || token.Length is < ShortestToken or > LongestToken || !token.StartsWith(TokenPrefix, StringComparison.Ordinal))
        {
            return false;
        }

        var separator = token.IndexOf(Separator, TokenPrefix.Length);
        if (separator < 0)
        {
            return false;
        }

        var candidateKeyId = token[TokenPrefix.Length..separator];
        var candidatePayload = token[(separator + 1)..];
        if (!IsWellFormedKeyId(candidateKeyId) || !Canonical.Base64UrlText.IsUnpadded(candidatePayload, PayloadCharacters))
        {
            return false;
        }

        keyId = candidateKeyId;
        payload = candidatePayload;
        return true;
    }

    private static bool IsLowerAlphanumeric(char c) => char.IsAsciiLetterLower(c) || char.IsAsciiDigit(c);
}
