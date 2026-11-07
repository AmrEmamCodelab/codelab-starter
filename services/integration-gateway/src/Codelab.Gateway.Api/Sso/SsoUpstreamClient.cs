using System.Net.Http.Headers;
using Codelab.Gateway.Api.Relay;
using Codelab.Integration.Gateway;
using Microsoft.Extensions.Options;

namespace Codelab.Gateway.Api.Sso;

/// <summary>The OIDC provider as the SSO profile sees it (a port, so the JWKS cache is testable without HTTP).</summary>
internal interface ISsoUpstream
{
    /// <summary>POSTs the token request form, byte for byte, to the token endpoint.</summary>
    Task<UpstreamResult> ExchangeTokenAsync(byte[] form, CancellationToken cancellationToken);

    /// <summary>GETs the JWKS document.</summary>
    Task<UpstreamResult> GetJwksAsync(CancellationToken cancellationToken);
}

/// <summary>
/// The OIDC provider's typed client on the Codelab.Integration outbound engine (<see cref="ClientName"/>): validated
/// TLS, timeouts, retries of the JWKS GET only. Answers are read up to <see cref="GatewayRelayLimits.MaxResponseBytes"/>.
/// </summary>
internal sealed class SsoUpstreamClient(HttpClient http, IOptions<SsoRelayOptions> options) : ISsoUpstream
{
    /// <summary>The name of the outbound client and of its options.</summary>
    public const string ClientName = "gateway-sso-upstream";

    public async Task<UpstreamResult> ExchangeTokenAsync(byte[] form, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(form);
        var content = new ByteArrayContent(form);
        content.Headers.ContentType = new MediaTypeHeaderValue(GatewayMediaTypes.FormUrlEncoded);
        using var request = new HttpRequestMessage(HttpMethod.Post, options.Value.TokenEndpoint) { Content = content };
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue(GatewayMediaTypes.Json));
        return await UpstreamExchange.SendAsync(http, request, GatewayRelayLimits.MaxResponseBytes, cancellationToken);
    }

    public async Task<UpstreamResult> GetJwksAsync(CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, options.Value.JwksUri);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue(GatewayMediaTypes.Json));
        return await UpstreamExchange.SendAsync(http, request, GatewayRelayLimits.MaxResponseBytes, cancellationToken);
    }
}
