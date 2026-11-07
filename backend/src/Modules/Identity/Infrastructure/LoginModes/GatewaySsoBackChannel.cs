using System.Text.Json;
using Codelab.Identity.Oidc;
using Codelab.Integration.Classification;
using Codelab.Integration.Gateway;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Starter.Modules.Identity.Infrastructure.LoginModes;

/// <summary>
/// The SSO back channel of a backend zone without egress (<see cref="OidcBackChannel.GatewayRelay"/>): the token
/// request and the JWKS fetch go to the integration gateway's SSO relay profile through
/// <see cref="GatewayRelayClient"/> (every attempt signed with this API's ES256 service assertion). The gateway calls
/// the provider and returns its JSON verbatim. Never throws for a relay fault: a refusal of the request is
/// <see cref="OidcBackChannelOutcome.Rejected"/>, everything else <see cref="OidcBackChannelOutcome.Unavailable"/>,
/// with the gateway's code as the logged reason.
/// </summary>
/// <remarks>
/// A singleton (the OIDC provider and the key source are): the typed client is resolved per call, so its pooled
/// handler rotates as the client factory intends.
/// </remarks>
public sealed class GatewaySsoBackChannel(IServiceProvider services) : IOidcBackChannel
{
    /// <summary>The relay answers only a provider success; its status is not passed on.</summary>
    private const int StatusOk = (int)System.Net.HttpStatusCode.OK;

    /// <inheritdoc />
    public OidcBackChannel Mode => OidcBackChannel.GatewayRelay;

    /// <inheritdoc />
    public Task<OidcBackChannelResponse> RedeemCodeAsync(IReadOnlyCollection<KeyValuePair<string, string>> form, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(form);
        return RelayAsync(client => client.RelaySsoTokenAsync(new FormUrlEncodedContent(form), cancellationToken));
    }

    /// <inheritdoc />
    public Task<OidcBackChannelResponse> GetJwksAsync(CancellationToken cancellationToken) =>
        RelayAsync(client => client.GetSsoJwksAsync(cancellationToken));

    /// <summary>
    /// What a relay failure means for a sign-in: the provider (or the gateway on its behalf) refused the request ->
    /// Rejected; the gateway refused this API (401/403: the S2S key or audience is wrong), the profile is off, or
    /// anything is unreachable -> Unavailable (a deployment fault, never the user's).
    /// </summary>
    private static OidcBackChannelResponse Map(GatewayRelayException failure)
    {
        ArgumentNullException.ThrowIfNull(failure);
        var status = failure.Failure.StatusCode is { } code ? (int)code : (int?)null;
        var isRequestRefusal = failure.Code == GatewayFailureCodes.UpstreamRejected && failure.Failure.Kind != OutboundFailureKind.Unauthorized;
        return isRequestRefusal
            ? OidcBackChannelResponse.Rejected(status, failure.Code)
            : OidcBackChannelResponse.Unavailable(failure.Code);
    }

    private async Task<OidcBackChannelResponse> RelayAsync(Func<GatewayRelayClient, Task<JsonDocument>> call)
    {
        try
        {
            using var document = await call(services.GetRequiredService<GatewayRelayClient>());
            return OidcBackChannelResponse.Answered(StatusOk, document.RootElement.GetRawText());
        }
        catch (GatewayRelayException failure)
        {
            return Map(failure);
        }
    }
}

/// <summary>Registers the integration gateway client once, however many modules ask for it.</summary>
public static class GatewayClientRegistration
{
    /// <summary>
    /// Calls <c>AddCodelabGatewayClient</c> unless the typed <see cref="GatewayRelayClient"/> is already registered (a
    /// second call would add the signing handler twice).
    /// </summary>
    public static void AddGatewayClientOnce(this IServiceCollection services, IConfiguration configuration)
    {
        if (!services.Any(descriptor => descriptor.ServiceType == typeof(GatewayRelayClient)))
        {
            services.AddCodelabGatewayClient(configuration);
        }
    }
}
