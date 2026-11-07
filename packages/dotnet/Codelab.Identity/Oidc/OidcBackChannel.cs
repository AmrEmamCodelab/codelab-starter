using System.Net.Http.Headers;
using Microsoft.Extensions.Options;

namespace Codelab.Identity.Oidc;

/// <summary>How the server-to-provider calls (token, JWKS) reach the provider. Bound and stored as an integer.</summary>
public enum OidcBackChannel
{
    /// <summary>
    /// This host calls <see cref="OidcOptions.TokenEndpoint"/> and <see cref="OidcOptions.JwksUri"/> itself, over the
    /// named client <see cref="OidcHttpClient.Name"/>. Needs egress to the provider.
    /// </summary>
    Direct = 1,

    /// <summary>
    /// The calls go through the integration gateway's SSO relay profile (a backend zone without egress). The host
    /// registers an <see cref="IOidcBackChannel"/> whose <see cref="IOidcBackChannel.Mode"/> is this value before
    /// <c>AddCodelabOidc</c>; the browser-facing authorize and end_session redirects are unchanged.
    /// </summary>
    GatewayRelay = 2,
}

/// <summary>What became of one back-channel call. Stored nowhere; drives the caller's verdict.</summary>
public enum OidcBackChannelOutcome
{
    /// <summary>The provider (or the relay on its behalf) answered: <see cref="OidcBackChannelResponse.StatusCode"/> and <see cref="OidcBackChannelResponse.Body"/> are set.</summary>
    Answered = 1,

    /// <summary>The provider could not be reached (network, timeout, 5xx through a relay, relay misconfigured). A sign-in is unavailable, not refused.</summary>
    Unavailable = 2,

    /// <summary>The provider refused the request and the relay passed only the verdict on (no body). A sign-in is rejected.</summary>
    Rejected = 3,
}

/// <summary>
/// The answer of one back-channel call, made only through <see cref="Answered"/>, <see cref="Unavailable"/> and
/// <see cref="Rejected"/>. <see cref="Body"/> can hold tokens: <see cref="ToString"/> never prints it.
/// </summary>
public sealed class OidcBackChannelResponse
{
    private OidcBackChannelResponse(OidcBackChannelOutcome outcome, int? statusCode, string? body, string? failureReason)
    {
        Outcome = outcome;
        StatusCode = statusCode;
        Body = body;
        FailureReason = failureReason;
    }

    /// <summary>What became of the call.</summary>
    public OidcBackChannelOutcome Outcome { get; }

    /// <summary>The HTTP status the provider (or the relay) answered, when known.</summary>
    public int? StatusCode { get; }

    /// <summary>The response body (<see cref="OidcBackChannelOutcome.Answered"/> only; may hold tokens).</summary>
    public string? Body { get; }

    /// <summary>A short, secret-free reason for the log (an exception type or a relay code), for a failure.</summary>
    public string? FailureReason { get; }

    /// <summary>The provider answered <paramref name="statusCode"/> with <paramref name="body"/>.</summary>
    public static OidcBackChannelResponse Answered(int statusCode, string body)
    {
        ArgumentNullException.ThrowIfNull(body);
        return new(OidcBackChannelOutcome.Answered, statusCode, body, failureReason: null);
    }

    /// <summary>The provider could not be reached; <paramref name="reason"/> (secret-free) is logged.</summary>
    public static OidcBackChannelResponse Unavailable(string reason)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(reason);
        return new(OidcBackChannelOutcome.Unavailable, statusCode: null, body: null, reason);
    }

    /// <summary>The provider refused the request (through a relay that keeps the body back); <paramref name="reason"/> (secret-free) is logged.</summary>
    public static OidcBackChannelResponse Rejected(int? statusCode, string reason)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(reason);
        return new(OidcBackChannelOutcome.Rejected, statusCode, body: null, reason);
    }

    /// <summary>The outcome, the status and the reason; never the body.</summary>
    public override string ToString() =>
        string.Create(System.Globalization.CultureInfo.InvariantCulture, $"{Outcome} {StatusCode} {FailureReason}").TrimEnd();
}

/// <summary>
/// The server-to-provider calls of an OIDC sign-in: the token request and the JWKS fetch. The package registers the
/// <see cref="OidcBackChannel.Direct"/> implementation (the named client); a host without egress registers its own
/// (for example over the integration gateway relay) before <c>AddCodelabOidc</c>. Implementations never throw for a
/// provider or transport fault: they answer <see cref="OidcBackChannelOutcome.Unavailable"/> or
/// <see cref="OidcBackChannelOutcome.Rejected"/>. A cancellation of the caller's token propagates.
/// </summary>
public interface IOidcBackChannel
{
    /// <summary>Which back channel this is; start-up refuses a mismatch with <see cref="OidcOptions.BackChannel"/>.</summary>
    OidcBackChannel Mode { get; }

    /// <summary>Posts the token request form (<c>application/x-www-form-urlencoded</c>, fields in order) and returns the answer.</summary>
    /// <param name="form">The token request fields; may hold the code, the verifier and the client secret.</param>
    /// <param name="cancellationToken">The caller's cancellation.</param>
    Task<OidcBackChannelResponse> RedeemCodeAsync(IReadOnlyCollection<KeyValuePair<string, string>> form, CancellationToken cancellationToken);

    /// <summary>Fetches the provider's JSON Web Key Set.</summary>
    Task<OidcBackChannelResponse> GetJwksAsync(CancellationToken cancellationToken);
}

/// <summary>
/// <see cref="OidcBackChannel.Direct"/>: the token endpoint and the JWKS URI over the named client
/// <see cref="OidcHttpClient.Name"/> (configured timeout, response size cap, platform certificate validation).
/// </summary>
internal sealed class DirectOidcBackChannel(IHttpClientFactory clients, IOptions<OidcOptions> options) : IOidcBackChannel
{
    public OidcBackChannel Mode => OidcBackChannel.Direct;

    public Task<OidcBackChannelResponse> RedeemCodeAsync(IReadOnlyCollection<KeyValuePair<string, string>> form, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(form);
        var request = new HttpRequestMessage(HttpMethod.Post, options.Value.TokenEndpoint) { Content = new FormUrlEncodedContent(form) };
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue(OidcProtocol.JsonMediaType));
        return SendAsync(request, cancellationToken);
    }

    public Task<OidcBackChannelResponse> GetJwksAsync(CancellationToken cancellationToken) =>
        SendAsync(new HttpRequestMessage(HttpMethod.Get, options.Value.JwksUri), cancellationToken);

    private async Task<OidcBackChannelResponse> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        using (request)
        {
            request.Headers.ExpectContinue = false;
            try
            {
                using var response = await clients.CreateClient(OidcHttpClient.Name).SendAsync(request, cancellationToken);
                return OidcBackChannelResponse.Answered((int)response.StatusCode, await response.Content.ReadAsStringAsync(cancellationToken));
            }
            catch (Exception failure) when (OidcHttpClient.IsTransportFailure(failure, cancellationToken))
            {
                return OidcBackChannelResponse.Unavailable(failure.GetType().Name);
            }
        }
    }
}

/// <summary>
/// Refuses the start when the registered <see cref="IOidcBackChannel"/> is not the configured
/// <see cref="OidcOptions.BackChannel"/>: a relay deployment that forgot to register the relay would otherwise call the
/// provider directly from a zone without egress, and fail every sign-in at run time.
/// </summary>
/// <remarks>
/// The back channel is resolved when the options are validated, not injected: the direct one reads these options, so
/// injecting it would make the options depend on themselves.
/// </remarks>
internal sealed class OidcBackChannelValidator(IServiceProvider services) : IValidateOptions<OidcOptions>
{
    public ValidateOptionsResult Validate(string? name, OidcOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        if (!options.Enabled || !Enum.IsDefined(options.BackChannel))
        {
            return ValidateOptionsResult.Success;
        }

        var backChannel = services.GetRequiredService<IOidcBackChannel>();
        if (options.BackChannel == backChannel.Mode)
        {
            return ValidateOptionsResult.Success;
        }

        return ValidateOptionsResult.Fail(
            $"{OidcOptions.SectionName}:{nameof(OidcOptions.BackChannel)} is {(int)options.BackChannel} ({options.BackChannel}), but the registered "
            + $"back channel is {(int)backChannel.Mode} ({backChannel.Mode}). Register an {nameof(IOidcBackChannel)} of that mode before AddCodelabOidc "
            + "(the host's gateway relay adapter), or change the setting.");
    }
}
