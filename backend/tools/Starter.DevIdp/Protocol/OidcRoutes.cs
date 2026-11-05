namespace Starter.DevIdp.Protocol;

/// <summary>The endpoint paths of the dev IdP (relative to the issuer).</summary>
public static class OidcRoutes
{
    /// <summary>OpenID Provider metadata.</summary>
    public const string Discovery = "/.well-known/openid-configuration";

    /// <summary>The authorization endpoint (GET: request, POST: the fixture-user picker form).</summary>
    public const string Authorize = "/authorize";

    /// <summary>The token endpoint (authorization_code grant).</summary>
    public const string Token = "/token";

    /// <summary>The JSON Web Key Set with the public signing key.</summary>
    public const string Jwks = "/jwks";

    /// <summary>The userinfo endpoint (Bearer access token).</summary>
    public const string UserInfo = "/userinfo";

    /// <summary>The RP-initiated logout endpoint.</summary>
    public const string EndSession = "/end-session";
}
