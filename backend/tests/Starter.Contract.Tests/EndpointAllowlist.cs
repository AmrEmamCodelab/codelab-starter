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

    /// <summary>The module that owns the lookup read API.</summary>
    public const string LookupsModule = "Lookups";

    /// <summary>The module that owns the caller's own notification feed.</summary>
    public const string NotificationsModule = "Notifications";

    /// <summary>The module that owns the public portal pages and the contact form.</summary>
    public const string PortalModule = "Portal";

    /// <summary>Wire members that legitimately carry a number: lookup ids seeded by migrations (Type.Member), passed to the PublicId contract.</summary>
    public static IReadOnlyCollection<string> NumericLookupIdMembers { get; } = ["SubmitContactMessageCommand.TopicId"];

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
        new("GET /api/lookups/{type}", EndpointAccess.Authenticated, LookupsModule, "Any signed-in user may read the active values of a lookup (dropdown sources); the admin view and activate/deactivate need lookups.view / lookups.manage."),
        new("GET /api/notifications", EndpointAccess.Authenticated, NotificationsModule, "Any signed-in user may read their own notifications; the feed is scoped to the caller."),
        new("GET /api/notifications/unread-count", EndpointAccess.Authenticated, NotificationsModule, "Any signed-in user may read their own unread count (the bell badge)."),
        new("POST /api/notifications/{id:publicid}/read", EndpointAccess.Authenticated, NotificationsModule, "Any signed-in user may mark their own notification read; another user's id answers 404."),
        new("POST /api/portal/contact-messages", EndpointAccess.Anonymous, PortalModule, "The public contact form: the people most likely to write have no account. Rate-limited per client (the anonymous-write policy, trusted-proxy aware); answers only the reference number it generated."),
        new("GET /api/portal/content/{page}", EndpointAccess.Anonymous, PortalModule, "The public FAQ, service guide, privacy and terms pages are read before and without a session; published text only, never per-user data."),
        new("GET /api/features", EndpointAccess.Anonymous, EndpointOwners.Kernel, "Feature flags are read before sign-in; flags only, never identities."),
        new("GET /api/security/session-signing-key", EndpointAccess.Authenticated, EndpointOwners.Platform, "Issues the caller's session signing key right after sign-in; Codelab.Security requires authentication."),
        new("* /health/live", EndpointAccess.Anonymous, EndpointOwners.Platform, "Liveness probe for the load balancer; runs no check."),
        new("* /health/ready", EndpointAccess.Anonymous, EndpointOwners.Platform, "Readiness probe for the load balancer."),
        new("GET /openapi/{documentName}.json", EndpointAccess.Anonymous, EndpointOwners.Kernel, "The OpenAPI document, mapped in Development only."),
    ];
}
