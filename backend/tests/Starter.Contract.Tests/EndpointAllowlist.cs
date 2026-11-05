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
        new("GET /api/me/permissions", EndpointAccess.Authenticated, AccessModule, "Any signed-in user may read their own permission keys."),
        new("GET /api/features", EndpointAccess.Anonymous, EndpointOwners.Kernel, "Feature flags are read before sign-in; flags only, never identities."),
        new("GET /api/security/session-signing-key", EndpointAccess.Authenticated, EndpointOwners.Platform, "Issues the caller's session signing key right after sign-in; Codelab.Security requires authentication."),
        new("* /health/live", EndpointAccess.Anonymous, EndpointOwners.Platform, "Liveness probe for the load balancer; runs no check."),
        new("* /health/ready", EndpointAccess.Anonymous, EndpointOwners.Platform, "Readiness probe for the load balancer."),
        new("GET /openapi/{documentName}.json", EndpointAccess.Anonymous, EndpointOwners.Kernel, "The OpenAPI document, mapped in Development only."),
    ];
}
