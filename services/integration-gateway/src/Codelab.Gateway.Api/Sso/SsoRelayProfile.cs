using Codelab.Errors;
using Codelab.Gateway.Api.Relay;
using Codelab.Integration.Gateway;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;

namespace Codelab.Gateway.Api.Sso;

/// <summary>
/// The SSO relay profile (<see cref="GatewayProfileKey.Sso"/>): the OIDC back channel of a backend without egress.
/// POST <see cref="GatewayRoutes.SsoToken"/> passes the token request form through to the provider's token endpoint and
/// returns the provider's JSON verbatim (never cached). GET <see cref="GatewayRoutes.SsoJwks"/> returns the provider's
/// JWKS document from <see cref="JwksCache"/>.
/// </summary>
internal sealed class SsoRelayProfile(IOptions<SsoRelayOptions> options) : IGatewayProfile, IRelayUpstreams
{
    private static readonly RelayStep TokenStep = new(GatewayProfileKey.Sso, RelayOperations.Token);
    private static readonly RelayStep JwksStep = new(GatewayProfileKey.Sso, RelayOperations.Jwks);

    public string Key => GatewayProfileKey.Sso;

    public IEnumerable<Uri?> UpstreamAddresses => [options.Value.TokenEndpoint, options.Value.JwksUri];

    public void Map(IEndpointRouteBuilder endpoints)
    {
        ArgumentNullException.ThrowIfNull(endpoints);
        endpoints.MapPost(GatewayRoutes.SsoToken, RelayTokenAsync);
        endpoints.MapGet(GatewayRoutes.SsoJwks, RelayJwksAsync);
    }

    internal static async Task<IResult> RelayTokenAsync(HttpContext context, ISsoUpstream upstream, ILogger<SsoRelayProfile> logger)
    {
        ArgumentNullException.ThrowIfNull(context);
        var cancellationToken = context.RequestAborted;
        RelayResponses.NoStore(context.Response);
        if (!RelayRefusals.HasMediaType(context.Request, GatewayMediaTypes.FormUrlEncoded))
        {
            return RelayResponses.Refused(logger, TokenStep, RelayRefusals.Malformed($"The token request must be {GatewayMediaTypes.FormUrlEncoded}."), ErrorCodes.Malformed);
        }

        var form = await BoundedBody.ReadAsync(context.Request.Body, GatewayRelayLimits.MaxSsoTokenFormBytes, cancellationToken);
        if (form is null)
        {
            return RelayResponses.Refused(logger, TokenStep, RelayRefusals.TooLarge(), ErrorCodes.PayloadTooLarge);
        }

        if (TokenRequestGuard.Problem(form) is { } problem)
        {
            return RelayResponses.Refused(logger, TokenStep, RelayRefusals.Malformed(problem), ErrorCodes.Malformed);
        }

        var result = await upstream.ExchangeTokenAsync(form, cancellationToken);
        if (result.Succeeded && !SsoDocuments.IsJsonObject(result.Body))
        {
            result = UpstreamResult.Failed(UpstreamFailureMapper.Unusable(result.Status!.Value));
        }

        return RelayResponses.Json(logger, TokenStep, result);
    }

    internal static async Task<IResult> RelayJwksAsync(HttpContext context, JwksCache cache, ILogger<SsoRelayProfile> logger)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(cache);
        return RelayResponses.Json(logger, JwksStep, await cache.GetAsync(context.RequestAborted));
    }
}

/// <summary>Registers the SSO relay profile; its upstream client and checks only when the profile is enabled.</summary>
internal static class SsoRelayRegistration
{
    public static IServiceCollection AddSsoRelay(this IServiceCollection services, IConfiguration configuration, bool enabled)
    {
        services.AddGatewayProfile<SsoRelayProfile>();
        var options = services.AddOptions<SsoRelayOptions>().Bind(configuration.GetSection(SsoRelayOptions.SectionName));
        if (!enabled)
        {
            return services;
        }

        options.ValidateOnStart();
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IValidateOptions<SsoRelayOptions>, SsoRelayOptionsValidator>());
        UpstreamClients.Add<SsoUpstreamClient, SsoRelayOptions>(services, SsoUpstreamClient.ClientName, static sso => sso.TokenEndpoint);
        services.AddTransient<ISsoUpstream>(static provider => provider.GetRequiredService<SsoUpstreamClient>());
        services.TryAddSingleton<JwksCacheStore>();
        services.AddTransient<JwksCache>();
        return services;
    }
}
