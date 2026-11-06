using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text.Json;
using Codelab.Security;
using Codelab.Security.Canonical;
using Codelab.Security.Signing;
using Microsoft.Net.Http.Headers;
using Starter.Integration.Tests.Support;

namespace Starter.Integration.Tests.Attachments.Http;

/// <summary>
/// A GET as the SPA sends it from its own origin: bearer token, <c>Origin</c>, and the request proof signed with the
/// session key the signing-key endpoint issued (exactly what @codelab/ng-security does), so the response goes through
/// the response signer as it does in the browser.
/// </summary>
public sealed class SignedBrowserCall(HttpClient http, SecurityProtocol protocol) : IDisposable
{
    private const string BearerScheme = "Bearer";
    private const int NonceBytes = 16;
    private const string PreflightHeaderSeparator = ",";
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    /// <summary>Sends the signed GET of <paramref name="path"/> (no query) as <paramref name="origin"/>.</summary>
    public async Task<HttpResponseMessage> GetAsync(string path, string accessToken, string origin)
    {
        var key = await SigningKeyAsync(accessToken);
        using var request = new HttpRequestMessage(HttpMethod.Get, new Uri(path, UriKind.Relative));
        request.Headers.Authorization = new AuthenticationHeaderValue(BearerScheme, accessToken);
        request.Headers.Add(HeaderNames.Origin, origin);
        request.Headers.Add(protocol.SignatureHeaderName, Proof(key, path));
        return await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead);
    }

    /// <summary>
    /// The CORS preflight the browser sends before that GET: <c>OPTIONS</c> with <c>Origin</c>, the method and the two
    /// non-safelisted request headers (Authorization and the request proof), lower-cased as browsers send them.
    /// </summary>
    public async Task<HttpResponseMessage> PreflightAsync(string path, string origin)
    {
        using var request = new HttpRequestMessage(HttpMethod.Options, new Uri(path, UriKind.Relative));
        request.Headers.Add(HeaderNames.Origin, origin);
        request.Headers.Add(HeaderNames.AccessControlRequestMethod, HttpMethod.Get.Method);
        request.Headers.Add(
            HeaderNames.AccessControlRequestHeaders,
            string.Join(PreflightHeaderSeparator, HeaderNames.Authorization.ToLowerInvariant(), protocol.SignatureHeaderName.ToLowerInvariant()));
        return await http.SendAsync(request);
    }

    /// <summary>
    /// The GET as a download manager replays it: bearer token and <c>Origin</c>, but no request proof (the server
    /// refuses it 401 <c>signature_missing</c>).
    /// </summary>
    public async Task<HttpResponseMessage> GetWithoutProofAsync(string path, string accessToken, string origin)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, new Uri(path, UriKind.Relative));
        request.Headers.Authorization = new AuthenticationHeaderValue(BearerScheme, accessToken);
        request.Headers.Add(HeaderNames.Origin, origin);
        return await http.SendAsync(request);
    }

    /// <inheritdoc />
    public void Dispose() => http.Dispose();

    private string Proof(SessionSigningKeyResponse key, string path)
    {
        var fields = new SignedRequestFields(
            Audience: key.Audience,
            Method: HttpMethod.Get.Method,
            Path: path,
            Query: string.Empty,
            BodyHash: SignatureCanonicalForm.HashBody([]),
            Timestamp: key.ServerTimeUnix.ToString(CultureInfo.InvariantCulture),
            Nonce: Base64UrlText.Encode(RandomNumberGenerator.GetBytes(NonceBytes)));
        var mac = HmacSignature.Compute(Convert.FromBase64String(key.SessionSigningKey), new SignatureCanonicalForm(protocol).RequestBytes(fields));
        Assert.True(SignatureHeader.TryCreate(fields.Timestamp, fields.Nonce, mac, out var header));
        return header.Format();
    }

    private async Task<SessionSigningKeyResponse> SigningKeyAsync(string accessToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, new Uri(SigningKeyEndpoint.DefaultPattern, UriKind.Relative));
        request.Headers.Authorization = new AuthenticationHeaderValue(BearerScheme, accessToken);
        using var response = await http.SendAsync(request);
        var body = await ApiClient.ReadAsync(response, HttpStatusCode.OK);
        return body.Deserialize<SessionSigningKeyResponse>(Json)!;
    }
}
