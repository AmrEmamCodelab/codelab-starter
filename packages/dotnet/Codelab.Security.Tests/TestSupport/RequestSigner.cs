using System.Globalization;
using System.Security.Cryptography;
using Codelab.Security.Canonical;
using Codelab.Security.Signing;

namespace Codelab.Security.Tests.TestSupport;

/// <summary>The client half of request signing, as the browser does it, built only from the package's public API.</summary>
internal sealed class RequestSigner(SecurityProtocol protocol, byte[] sessionKey, string audience)
{
    private const int NonceBytes = 16;

    public static RequestSigner ForSession(SecurityProtocol protocol, string masterKeyBase64, TestSession session, string audience)
    {
        var key = new SessionKeyDerivation(protocol).Derive(Convert.FromBase64String(masterKeyBase64), new SessionIdentity(session.UserId, session.SessionId));
        return new RequestSigner(protocol, key, audience);
    }

    public byte[] SessionKey => sessionKey;

    public static string NewNonce() => Base64UrlText.Encode(RandomNumberGenerator.GetBytes(NonceBytes));

    /// <summary>The proof header value for a request.</summary>
    public string Proof(SignedRequestFields fields)
    {
        var mac = HmacSignature.Compute(sessionKey, new SignatureCanonicalForm(protocol).RequestBytes(fields));
        SignatureHeader.TryCreate(fields.Timestamp, fields.Nonce, mac, out var header);
        return header!.Format();
    }

    /// <summary>Signs <paramref name="request"/> as sent at <paramref name="now"/>.</summary>
    public string Sign(HttpRequestMessage request, DateTimeOffset now, byte[]? body = null, string? nonce = null, string? bodyHash = null)
    {
        var uri = request.RequestUri!;
        var fields = new SignedRequestFields(
            Audience: audience,
            Method: request.Method.Method,
            Path: uri.IsAbsoluteUri ? uri.AbsolutePath : uri.OriginalString.Split('?')[0],
            Query: QueryOf(uri),
            BodyHash: bodyHash ?? SignatureCanonicalForm.HashBody(body ?? []),
            Timestamp: now.ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture),
            Nonce: nonce ?? NewNonce());
        var proof = Proof(fields);
        request.Headers.Remove(protocol.SignatureHeaderName);
        request.Headers.Add(protocol.SignatureHeaderName, proof);
        return fields.Nonce;
    }

    /// <summary>True when the response signature verifies for the request nonce.</summary>
    public async Task<bool> VerifyResponseAsync(HttpResponseMessage response, string requestNonce)
    {
        if (!response.Headers.TryGetValues(protocol.ResponseSignatureHeaderName, out var values) || !ResponseSignatureHeader.TryParse(values.Single(), out var mac))
        {
            return false;
        }

        var body = await response.Content.ReadAsByteArrayAsync();
        var fields = new SignedResponseFields(requestNonce, (int)response.StatusCode, response.Content.Headers.ContentType?.ToString() ?? string.Empty, SignatureCanonicalForm.HashBody(body));
        return HmacSignature.Verify(sessionKey, new SignatureCanonicalForm(protocol).ResponseBytes(fields), mac);
    }

    private static string QueryOf(Uri uri)
    {
        var query = uri.IsAbsoluteUri ? uri.Query : (uri.OriginalString.Contains('?', StringComparison.Ordinal) ? "?" + uri.OriginalString.Split('?', 2)[1] : string.Empty);
        return query.StartsWith('?') ? query[1..] : query;
    }
}
