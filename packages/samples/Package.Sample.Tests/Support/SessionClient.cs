using System.Globalization;
using System.Net.Http.Headers;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Codelab.Security;
using Codelab.Security.Canonical;
using Codelab.Security.Sessions;
using Codelab.Security.Signing;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace Package.Sample.Tests.Support;

/// <summary>One signed request as sent: the response and the nonce the response signature is bound to.</summary>
internal sealed record SignedExchange(HttpResponseMessage Response, string Nonce, string Proof);

/// <summary>
/// A logged-in browser session against the sample, built only from the packages' public API: a bearer token
/// carrying <c>sub</c>, <c>sid</c>, <c>skid</c> and <c>sver</c>, and the client half of request signing (the
/// session key is derived from the master key the host was configured with, as the key endpoint would hand it out).
/// </summary>
internal sealed class SessionClient
{
    private const int NonceBytes = 16;
    private const string JsonMediaType = "application/json";
    private static readonly TimeSpan TokenLifetime = TimeSpan.FromMinutes(5);

    private readonly SecurityProtocol _protocol;
    private readonly byte[] _sessionKey;
    private readonly HttpClient _http;

    public SessionClient(SampleHost host, string userId)
    {
        UserId = userId;
        SessionId = Guid.NewGuid().ToString("N");
        _protocol = host.Protocol;
        _http = host.NewClient();
        Token = MintToken(host.Keys.TokenSigningKey, userId, SessionId);
        _sessionKey = new SessionKeyDerivation(_protocol).Derive(
            Convert.FromBase64String(host.Keys.SigningMasterKey), new SessionIdentity(userId, SessionId));
    }

    public string UserId { get; }

    public string SessionId { get; }

    public string Token { get; }

    /// <summary>Sends a signed request; <paramref name="proof"/> replaces the computed proof (replay and tamper tests).</summary>
    public async Task<SignedExchange> SendAsync(HttpMethod method, string path, string? body = null, string? proof = null, string? signedBody = null)
    {
        using var request = new HttpRequestMessage(method, path);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", Token);
        if (body is not null)
        {
            request.Content = new StringContent(body, Encoding.UTF8, JsonMediaType);
        }

        var nonce = Base64UrlText.Encode(RandomNumberGenerator.GetBytes(NonceBytes));
        var computed = Proof(method, path, Encoding.UTF8.GetBytes(signedBody ?? body ?? string.Empty), nonce);
        request.Headers.Add(_protocol.SignatureHeaderName, proof ?? computed);
        var response = await _http.SendAsync(request);
        return new SignedExchange(response, nonce, proof ?? computed);
    }

    /// <summary>Sends the request with the bearer token but without a signature.</summary>
    public async Task<HttpResponseMessage> SendUnsignedAsync(HttpMethod method, string path, string? body = null)
    {
        using var request = new HttpRequestMessage(method, path);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", Token);
        if (body is not null)
        {
            request.Content = new StringContent(body, Encoding.UTF8, JsonMediaType);
        }

        return await _http.SendAsync(request);
    }

    /// <summary>True when the response carries a valid response signature for the request's nonce.</summary>
    public async Task<bool> VerifiesAsync(SignedExchange exchange)
    {
        var response = exchange.Response;
        if (!response.Headers.TryGetValues(_protocol.ResponseSignatureHeaderName, out var values)
            || !ResponseSignatureHeader.TryParse(values.Single(), out var mac))
        {
            return false;
        }

        var body = await response.Content.ReadAsByteArrayAsync();
        var fields = new SignedResponseFields(
            RequestNonce: exchange.Nonce,
            Status: (int)response.StatusCode,
            ContentType: response.Content.Headers.ContentType?.ToString() ?? string.Empty,
            BodyHash: SignatureCanonicalForm.HashBody(body));
        return HmacSignature.Verify(_sessionKey, new SignatureCanonicalForm(_protocol).ResponseBytes(fields), mac);
    }

    private string Proof(HttpMethod method, string path, byte[] body, string nonce)
    {
        var timestamp = TimeProvider.System.GetUtcNow().ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture);
        var fields = new SignedRequestFields(
            Audience: SampleSettings.Audience,
            Method: method.Method,
            Path: path,
            Query: string.Empty,
            BodyHash: SignatureCanonicalForm.HashBody(body),
            Timestamp: timestamp,
            Nonce: nonce);
        var mac = HmacSignature.Compute(_sessionKey, new SignatureCanonicalForm(_protocol).RequestBytes(fields));
        return SignatureHeader.TryCreate(timestamp, nonce, mac, out var header)
            ? header.Format()
            : throw new InvalidOperationException("The test signer built an invalid signature header.");
    }

    private static string MintToken(string signingKey, string userId, string sessionId)
    {
        var now = TimeProvider.System.GetUtcNow().UtcDateTime;
        return new JsonWebTokenHandler().CreateToken(new SecurityTokenDescriptor
        {
            Issuer = SampleSettings.Audience,
            Audience = SampleSettings.Audience,
            IssuedAt = now,
            NotBefore = now,
            Expires = now + TokenLifetime,
            Subject = new ClaimsIdentity(
            [
                new Claim(SecurityClaimTypes.Subject, userId),
                new Claim(SecurityClaimTypes.SessionId, sessionId),
                new Claim(SecurityClaimTypes.SessionKeyId, SampleSettings.SigningKeyId),
                new Claim(SecurityClaimTypes.SessionVersion, "0"),
            ]),
            SigningCredentials = new SigningCredentials(new SymmetricSecurityKey(Convert.FromBase64String(signingKey)), SecurityAlgorithms.HmacSha256),
        });
    }
}
