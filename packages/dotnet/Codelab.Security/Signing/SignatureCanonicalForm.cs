using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Codelab.Security.Canonical;

namespace Codelab.Security.Signing;

/// <summary>The fields a request proof covers. <see cref="Query"/> is the raw query without the leading <c>?</c>.</summary>
public sealed record SignedRequestFields(
    string Audience,
    string Method,
    string Path,
    string Query,
    string BodyHash,
    string Timestamp,
    string Nonce);

/// <summary>The fields a response signature covers; <see cref="RequestNonce"/> binds the response to its request.</summary>
public sealed record SignedResponseFields(string RequestNonce, int Status, string ContentType, string BodyHash);

/// <summary>
/// The request and response canonical forms of <c>{Prefix}-SIG-v1</c>, built on <see cref="CanonicalFraming"/>.
/// <para>
/// Request: profile <c>{Prefix}-SIG-v1/REQ</c>, fields <c>[version, audience, method, path, query, bodyHash,
/// timestamp, nonce]</c>; the method is the allowlist literal and the path is normalised here so no caller can forget.
/// Response: profile <c>{Prefix}-SIG-v1/RESP</c>, fields <c>[version, requestNonce, status, contentType, bodyHash]</c>.
/// A request form can never be read as a response form (different profile and field count).
/// </para>
/// </summary>
public sealed class SignatureCanonicalForm
{
    /// <summary>The body hash used when an endpoint is exempt from body coverage.</summary>
    public const string UnsignedBody = "UNSIGNED-BODY";

    /// <summary>The number of framed fields in a request form.</summary>
    public const int RequestFieldCount = 8;

    /// <summary>The number of framed fields in a response form.</summary>
    public const int ResponseFieldCount = 5;

    private readonly SecurityProtocol _protocol;
    private readonly CanonicalFraming _framing;

    /// <summary>Creates the forms for <paramref name="protocol"/>.</summary>
    public SignatureCanonicalForm(SecurityProtocol protocol)
    {
        ArgumentNullException.ThrowIfNull(protocol);
        _protocol = protocol;
        _framing = new CanonicalFraming(protocol);
    }

    /// <summary>The request canonical form.</summary>
    /// <exception cref="ArgumentOutOfRangeException">The method is not in the canonical allowlist.</exception>
    public string Request(SignedRequestFields fields)
    {
        ArgumentNullException.ThrowIfNull(fields);
        return _framing.Build(
            _protocol.RequestProfile,
            _protocol.SignatureVersion,
            fields.Audience,
            CanonicalText.CanonicalMethod(fields.Method),
            CanonicalText.NormalizePath(fields.Path),
            fields.Query,
            fields.BodyHash,
            fields.Timestamp,
            fields.Nonce);
    }

    /// <summary>The UTF-8 bytes of <see cref="Request"/>.</summary>
    public byte[] RequestBytes(SignedRequestFields fields) => Encoding.UTF8.GetBytes(Request(fields));

    /// <summary>The response canonical form (status in invariant digits).</summary>
    public string Response(SignedResponseFields fields)
    {
        ArgumentNullException.ThrowIfNull(fields);
        return _framing.Build(
            _protocol.ResponseProfile,
            _protocol.SignatureVersion,
            fields.RequestNonce,
            fields.Status.ToString(CultureInfo.InvariantCulture),
            fields.ContentType,
            fields.BodyHash);
    }

    /// <summary>The UTF-8 bytes of <see cref="Response"/>.</summary>
    public byte[] ResponseBytes(SignedResponseFields fields) => Encoding.UTF8.GetBytes(Response(fields));

    /// <summary>The body hash field: unpadded base64url SHA-256 of the exact body bytes (an empty body hashes too).</summary>
    public static string HashBody(ReadOnlySpan<byte> body)
    {
        Span<byte> digest = stackalloc byte[SHA256.HashSizeInBytes];
        SHA256.HashData(body, digest);
        return Base64UrlText.Encode(digest);
    }
}
