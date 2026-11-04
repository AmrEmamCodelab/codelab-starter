using System.Diagnostics.CodeAnalysis;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Codelab.Security.Canonical;

namespace Codelab.Security.ServiceAuth;

/// <summary>The claims of one service assertion: who, for whom, when, and exactly which request it covers.</summary>
public sealed record ServiceAssertionClaims(
    [property: JsonPropertyName("iss")] string Issuer,
    [property: JsonPropertyName("aud")] string Audience,
    [property: JsonPropertyName("iat")] long IssuedAtUnix,
    [property: JsonPropertyName("exp")] long ExpiresAtUnix,
    [property: JsonPropertyName("jti")] string Id,
    [property: JsonPropertyName("htm")] string Method,
    [property: JsonPropertyName("htu")] string Target,
    [property: JsonPropertyName("bh")] string BodyHash);

/// <summary>
/// The compact JWS form of a service assertion, ES256 only: <c>base64url(header).base64url(claims).base64url(sig)</c>,
/// header <c>{"alg":"ES256","typ":"{Prefix}-S2S-v1","kid":...}</c>, signature in IEEE P1363 (r||s) form. Hand-rolled
/// on purpose: the parser accepts exactly one algorithm, so there is no algorithm negotiation to attack.
/// </summary>
public static class ServiceAssertionToken
{
    /// <summary>The one accepted algorithm.</summary>
    public const string Algorithm = "ES256";

    /// <summary>P-256 key size.</summary>
    public const int KeySizeBits = 256;

    /// <summary>The longest token accepted (bounded before parsing).</summary>
    public const int MaxTokenLength = 4096;

    private const char Separator = '.';
    private const int PartCount = 3;
    private const int SignatureBytes = 64;
    private const int HeaderPart = 0;
    private const int ClaimsPart = 1;
    private const int SignaturePart = 2;

    /// <summary>The canonical request target bound into <c>htu</c>: the normalised path, then <c>?query</c> when present.</summary>
    public static string Target(string path, string query)
    {
        ArgumentNullException.ThrowIfNull(path);
        ArgumentNullException.ThrowIfNull(query);
        var trimmedQuery = query.StartsWith('?') ? query[1..] : query;
        var normalized = CanonicalText.NormalizePath(path);
        return trimmedQuery.Length == 0 ? normalized : normalized + "?" + trimmedQuery;
    }

    /// <summary>Signs <paramref name="claims"/> with <paramref name="key"/> under <paramref name="header"/>.</summary>
    public static string Create(ServiceAssertionHeader header, ServiceAssertionClaims claims, ECDsa key)
    {
        ArgumentNullException.ThrowIfNull(key);
        var signingInput = Base64UrlText.Encode(JsonSerializer.SerializeToUtf8Bytes(header)) + Separator + Base64UrlText.Encode(JsonSerializer.SerializeToUtf8Bytes(claims));
        var signature = key.SignData(Encoding.ASCII.GetBytes(signingInput), HashAlgorithmName.SHA256, DSASignatureFormat.IeeeP1363FixedFieldConcatenation);
        return signingInput + Separator + Base64UrlText.Encode(signature);
    }

    /// <summary>Parses a token without verifying it; false for anything malformed (never throws).</summary>
    public static bool TryRead(string? token, [NotNullWhen(true)] out ParsedServiceAssertion? parsed)
    {
        parsed = null;
        var parts = token is { Length: > 0 and <= MaxTokenLength } ? token.Split(Separator) : [];
        if (parts.Length != PartCount
            || !Base64UrlText.TryDecode(parts[HeaderPart], out var headerBytes)
            || !Base64UrlText.TryDecode(parts[ClaimsPart], out var claimBytes)
            || !Base64UrlText.TryDecode(parts[SignaturePart], out var signature)
            || signature.Length != SignatureBytes)
        {
            return false;
        }

        var header = TryDeserialize<ServiceAssertionHeader>(headerBytes);
        var claims = TryDeserialize<ServiceAssertionClaims>(claimBytes);
        if (header is null || claims is null)
        {
            return false;
        }

        parsed = new ParsedServiceAssertion(header, claims, Encoding.ASCII.GetBytes(parts[HeaderPart] + Separator + parts[ClaimsPart]), signature);
        return true;
    }

    private static T? TryDeserialize<T>(byte[] json)
        where T : class
    {
        try
        {
            return JsonSerializer.Deserialize<T>(json, ReadOptions.Strict);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static class ReadOptions
    {
        internal static readonly JsonSerializerOptions Strict = new() { RespectRequiredConstructorParameters = true, RespectNullableAnnotations = true };
    }
}

/// <summary>The JWS header.</summary>
public sealed record ServiceAssertionHeader(
    [property: JsonPropertyName("alg")] string Algorithm,
    [property: JsonPropertyName("typ")] string Type,
    [property: JsonPropertyName("kid")] string KeyId);

/// <summary>A parsed (not yet verified) assertion.</summary>
public sealed record ParsedServiceAssertion(ServiceAssertionHeader Header, ServiceAssertionClaims Claims, byte[] SigningInput, byte[] Signature)
{
    /// <summary>True when the signature verifies under <paramref name="key"/>.</summary>
    public bool VerifySignature(ECDsa key)
    {
        ArgumentNullException.ThrowIfNull(key);
        return key.VerifyData(SigningInput, Signature, HashAlgorithmName.SHA256, DSASignatureFormat.IeeeP1363FixedFieldConcatenation);
    }
}
