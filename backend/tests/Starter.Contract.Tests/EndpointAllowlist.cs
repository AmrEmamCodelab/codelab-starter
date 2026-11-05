using Starter.Contract.Tests.Support;

namespace Starter.Contract.Tests;

/// <summary>
/// The CLOSED list of endpoints without a permission key (contract section 4). Every other endpoint must carry
/// <c>[HasPermission(...)]</c>. Adding a line here is a security decision: it needs a reason and a reviewer.
/// Keys are <c>METHOD /route</c> exactly as the routing table prints them.
/// </summary>
public static class EndpointAllowlist
{
    /// <summary>The module that owns the authentication endpoints.</summary>
    public const string IdentityModule = "Identity";

    /// <summary>The module that owns the caller's own permission list.</summary>
    public const string AccessModule = "Access";

    /// <summary>Every allowlisted endpoint.</summary>
    public static IReadOnlyList<AllowlistEntry> Entries { get; } =
    [
        new("POST /api/auth/login", EndpointAccess.Anonymous, IdentityModule, "Signing in: the caller has no session yet."),
        new("POST /api/auth/refresh", EndpointAccess.Anonymous, IdentityModule, "The access token may have expired; the refresh token is the credential."),
        new("POST /api/auth/logout", EndpointAccess.Authenticated, IdentityModule, "Any signed-in user may end their own session."),
        new("POST /api/auth/activate", EndpointAccess.Anonymous, IdentityModule, "Redeeming an activation link: the account has no password yet; the sealed one-time ticket is the credential."),
        new("GET /api/auth/modes", EndpointAccess.Anonymous, IdentityModule, "The login page asks which login methods to offer before sign-in; mode flags only, never an identity."),
        new("POST /api/auth/ad/login", EndpointAccess.Anonymous, IdentityModule, "Signing in with a directory password: the caller has no session yet (404 unless Auth:Modes lists ad)."),
        new("GET /api/auth/sso/login", EndpointAccess.Anonymous, IdentityModule, "Starting single sign-on is a top-level browser navigation before any session (404 unless Auth:Modes lists sso)."),
        new("GET /api/auth/sso/signin-oidc", EndpointAccess.Anonymous, IdentityModule, "The identity provider's redirect back; the sealed sign-in cookie, the state and the code are the credentials."),
        new("POST /api/auth/sso/exchange", EndpointAccess.Anonymous, IdentityModule, "Trading the callback's one-time ticket for the session: the caller has no session yet; the ticket is the credential."),
        new("POST /api/auth/sso/logout", EndpointAccess.Authenticated, IdentityModule, "Any signed-in user may end their own session and learn the identity provider's end-session URL."),
        new("GET /api/me/permissions", EndpointAccess.Authenticated, AccessModule, "Any signed-in user may read their own permission keys."),
        new("GET /api/features", EndpointAccess.Anonymous, EndpointOwners.Kernel, "Feature flags are read before sign-in; flags only, never identities."),
        new("GET /api/security/session-signing-key", EndpointAccess.Authenticated, EndpointOwners.Platform, "Issues the caller's session signing key right after sign-in; Codelab.Security requires authentication."),
        new("* /health/live", EndpointAccess.Anonymous, EndpointOwners.Platform, "Liveness probe for the load balancer; runs no check."),
        new("* /health/ready", EndpointAccess.Anonymous, EndpointOwners.Platform, "Readiness probe for the load balancer."),
        new("GET /openapi/{documentName}.json", EndpointAccess.Anonymous, EndpointOwners.Kernel, "The OpenAPI document, mapped in Development only."),
    ];
}
