using System.Net.Http.Json;
using System.Text.Json;

namespace Codelab.Integration.Gateway;

/// <summary>
/// The backend's typed client of the integration gateway (registered by <c>AddCodelabGatewayClient</c>). It runs on
/// the Codelab outbound engine (certificate policy, timeouts, retry of GETs only), and every attempt carries a fresh
/// ES256 service assertion bound to its method, path and body. Every failure surfaces as a
/// <see cref="GatewayRelayException"/>; a caller cancellation surfaces as <see cref="OperationCanceledException"/>.
/// </summary>
public sealed class GatewayRelayClient
{
    /// <summary>The name of the typed client and of its outbound options.</summary>
    public const string Name = "codelab-integration-gateway";

    private readonly HttpClient _http;

    /// <summary>Creates the client on an HTTP client whose base address is the gateway.</summary>
    public GatewayRelayClient(HttpClient http) => _http = http ?? throw new ArgumentNullException(nameof(http));

    /// <summary>
    /// Relays an OIDC token request (POST <see cref="GatewayRoutes.SsoToken"/>) and returns the provider's JSON. The
    /// content is sent and disposed with the request (build it with <see cref="SsoTokenRelayRequest.ToContent"/> or
    /// pass the token request form as it is). Not retried: an authorization code is single-use.
    /// </summary>
    /// <exception cref="GatewayRelayException">The relay failed.</exception>
    public async Task<JsonDocument> RelaySsoTokenAsync(FormUrlEncodedContent content, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(content);
        using var request = new HttpRequestMessage(HttpMethod.Post, GatewayRoutes.Relative(GatewayRoutes.SsoToken)) { Content = content };
        return await SendForJsonAsync(request, cancellationToken);
    }

    /// <summary>Fetches the provider's JWKS document through GET <see cref="GatewayRoutes.SsoJwks"/>. Retried when transient.</summary>
    /// <exception cref="GatewayRelayException">The relay failed.</exception>
    public async Task<JsonDocument> GetSsoJwksAsync(CancellationToken cancellationToken = default)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, GatewayRoutes.Relative(GatewayRoutes.SsoJwks));
        return await SendForJsonAsync(request, cancellationToken);
    }

    /// <summary>Relays an SMS (POST <see cref="GatewayRoutes.Sms"/>). Not retried: the provider may not de-duplicate.</summary>
    /// <exception cref="ArgumentException">The request is invalid (<see cref="SmsRelayRequestValidator"/>).</exception>
    /// <exception cref="GatewayRelayException">The relay failed.</exception>
    public async Task<SmsRelayResult> RelaySmsAsync(SmsRelayRequest request, CancellationToken cancellationToken = default)
    {
        var problems = SmsRelayRequestValidator.Validate(request);
        if (problems.Count > 0)
        {
            throw new ArgumentException(string.Join(" ", problems), nameof(request));
        }

        return await PostJsonAsync<SmsRelayRequest, SmsRelayResult>(GatewayRoutes.Sms, request, cancellationToken);
    }

    /// <summary>
    /// The extension point for a project's own profile: POSTs <paramref name="request"/> as JSON to a relay route (one
    /// of its <c>GatewayRoutes</c>-style constants under <see cref="GatewayRoutes.RelayPrefix"/>) and reads the JSON
    /// answer. Signed, never retried, failures mapped like the built-in relays. Validate the request before calling.
    /// </summary>
    /// <exception cref="ArgumentException">The route is not a relay route.</exception>
    /// <exception cref="GatewayRelayException">The relay failed or the answer is not a <typeparamref name="TResponse"/>.</exception>
    public async Task<TResponse> PostJsonAsync<TRequest, TResponse>(string route, TRequest request, CancellationToken cancellationToken = default)
        where TResponse : class
    {
        using var message = new HttpRequestMessage(HttpMethod.Post, RelayTarget(route))
        {
            Content = JsonContent.Create(request, options: GatewayJson.Options),
        };
        var (status, body) = await SendAsync(message, cancellationToken);
        try
        {
            return JsonSerializer.Deserialize<TResponse>(body, GatewayJson.Options) ?? throw new GatewayRelayException(GatewayFailureMapper.Unusable(status));
        }
        catch (JsonException exception)
        {
            throw new GatewayRelayException(GatewayFailureMapper.Unusable(status), exception);
        }
    }

    /// <summary>
    /// The extension point for a project's own profile: GETs a relay route and returns its JSON. Signed, retried when
    /// transient, failures mapped like the built-in relays.
    /// </summary>
    /// <exception cref="ArgumentException">The route is not a relay route.</exception>
    /// <exception cref="GatewayRelayException">The relay failed.</exception>
    public async Task<JsonDocument> GetJsonAsync(string route, CancellationToken cancellationToken = default)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, RelayTarget(route));
        return await SendForJsonAsync(request, cancellationToken);
    }

    /// <summary>The relative target of a relay route; anything outside <c>/relay/{key}/...</c> is refused.</summary>
    internal static string RelayTarget(string route)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(route);
        var segments = route.Split('/');
        const int prefixSegment = 1;
        const int keySegment = 2;
        var isRelayRoute = segments.Length > keySegment
            && segments[0].Length == 0
            && "/" + segments[prefixSegment] == GatewayRoutes.RelayPrefix
            && GatewayProfileKey.IsValid(segments[keySegment])
            && !segments.Skip(keySegment).Any(segment => segment.Length == 0 || segment is "." or "..")
            && route.IndexOfAny(['?', '#', '\\', '%']) < 0;
        return isRelayRoute ? GatewayRoutes.Relative(route) : throw new ArgumentException($"'{route}' is not a relay route under {GatewayRoutes.RelayPrefix}/{{key}}.", nameof(route));
    }

    private async Task<JsonDocument> SendForJsonAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var (status, body) = await SendAsync(request, cancellationToken);
        try
        {
            return JsonDocument.Parse(body);
        }
        catch (JsonException exception)
        {
            throw new GatewayRelayException(GatewayFailureMapper.Unusable(status), exception);
        }
    }

    private async Task<(System.Net.HttpStatusCode Status, byte[] Body)> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        try
        {
            using var response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            var body = await BoundedContentReader.ReadAsync(response.Content, GatewayRelayLimits.MaxResponseBytes, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                var problem = body is null ? null : GatewayProblemBody.TryRead(body);
                throw new GatewayRelayException(GatewayFailureMapper.FromResponse(response.StatusCode, problem));
            }

            return body is null ? throw new GatewayRelayException(GatewayFailureMapper.Unusable(response.StatusCode)) : (response.StatusCode, body);
        }
        catch (Exception exception) when (GatewayFailureMapper.IsTransportFailure(exception, cancellationToken))
        {
            throw new GatewayRelayException(GatewayFailureMapper.FromException(exception), exception);
        }
    }
}
