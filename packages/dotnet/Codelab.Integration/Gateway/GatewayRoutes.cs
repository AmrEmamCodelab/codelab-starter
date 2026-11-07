namespace Codelab.Integration.Gateway;

/// <summary>
/// The routes of the integration gateway, written as the gateway maps them (rooted at the gateway's own path base).
/// The relay client calls them relative to its configured <c>BaseUrl</c>, so a gateway hosted under an IIS
/// application path keeps working.
/// </summary>
public static class GatewayRoutes
{
    /// <summary>The prefix of every relay route; a profile owns <c>/relay/{key}</c> and everything below it.</summary>
    public const string RelayPrefix = "/relay";

    /// <summary>
    /// POST: the OIDC token request, <c>application/x-www-form-urlencoded</c>, passed through to the provider's token
    /// endpoint. Returns the provider's JSON verbatim.
    /// </summary>
    public const string SsoToken = RelayPrefix + "/" + GatewayProfileKey.Sso + "/token";

    /// <summary>GET: the provider's JWKS document, verbatim.</summary>
    public const string SsoJwks = RelayPrefix + "/" + GatewayProfileKey.Sso + "/jwks";

    /// <summary>POST: an <see cref="SmsRelayRequest"/>; returns an <see cref="SmsRelayResult"/>.</summary>
    public const string Sms = RelayPrefix + "/" + GatewayProfileKey.Sms;

    /// <summary>GET: liveness (the process answers). Anonymous.</summary>
    public const string HealthLive = "/health/live";

    /// <summary>GET: readiness (configuration valid and enabled profiles ready). Anonymous.</summary>
    public const string HealthReady = "/health/ready";

    /// <summary>The route-template suffix that catches every path below a profile prefix.</summary>
    public const string CatchAllSuffix = "/{**path}";

    /// <summary>The route prefix a profile owns: <c>/relay/{key}</c>.</summary>
    public static string ProfilePrefix(string key)
    {
        if (!GatewayProfileKey.IsValid(key))
        {
            throw new ArgumentException($"'{key}' is not a valid gateway profile key.", nameof(key));
        }

        return RelayPrefix + "/" + key;
    }

    /// <summary>A route relative to the gateway base address (no leading slash), as the relay client sends it.</summary>
    public static string Relative(string route)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(route);
        return route.TrimStart('/');
    }
}

/// <summary>The media types of the relay routes.</summary>
public static class GatewayMediaTypes
{
    /// <summary>The OIDC token request body.</summary>
    public const string FormUrlEncoded = "application/x-www-form-urlencoded";

    /// <summary>JSON request and response bodies.</summary>
    public const string Json = "application/json";

    /// <summary>A refusal (RFC 9457).</summary>
    public const string ProblemJson = "application/problem+json";
}
