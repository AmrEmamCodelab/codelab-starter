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

    /// <summary>The module that owns the generic record API.</summary>
    public const string RecordsModule = "Records";

    /// <summary>The deletable sample record type module.</summary>
    public const string ServiceRequestsModule = "ServiceRequests"; // codelab:module ServiceRequests

    /// <summary>The module that owns the Ops Console's /api/ops surface.</summary>
    public const string OpsModule = "Ops"; // codelab:module Ops

    /// <summary>Wire members that legitimately carry a number: lookup ids seeded by migrations (Type.Member), passed to the PublicId contract.</summary>
    public static IReadOnlyCollection<string> NumericLookupIdMembers { get; } = ["SubmitContactMessageCommand.TopicId"];

    /// <summary>
    /// Wire members that carry a lookup value's PublicId token as a plain string (Type.Member), passed to the PublicId
    /// contract. Each is already a codec-protected token (family <c>lookup-items</c>), never a raw id; it stays a string
    /// so an unreadable or retired token is the step's field error (<c>SERVICE_REQUESTS_CATEGORY_UNKNOWN</c>) instead of
    /// a body-level 400, as the wizard's client-side validation rules expect.
    /// </summary>
    public static IReadOnlyCollection<string> LookupTokenStringMembers { get; } =
    [
        "SaveServiceRequestDraftCommand.CategoryId", // codelab:module ServiceRequests
        "ServiceRequestInput.CategoryId", // codelab:module ServiceRequests
        "ServiceRequestDraftResponse.CategoryId", // codelab:module ServiceRequests
        "ServiceRequestDetailsResponse.CategoryId", // codelab:module ServiceRequests
    ];

    /// <summary>Every wire member the PublicId contract lets through, each listed above with its reason.</summary>
    /// <summary>Wire members that carry trace identifiers (correlation and W3C trace ids), not database ids (Type.Member).</summary>
    public static IReadOnlyCollection<string> TraceIdentifierMembers { get; } = ["ReferenceCodeResponse.CorrelationId", "ReferenceCodeResponse.TraceId"]; // codelab:module Ops

    public static IReadOnlyCollection<string> PublicIdExemptMembers => [.. NumericLookupIdMembers, .. LookupTokenStringMembers, .. TraceIdentifierMembers];

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
        new("GET /api/records/{recordType}/mine", EndpointAccess.Authenticated, RecordsModule, "The caller's own records of one record type; the record type's view-own permission comes from its registration and is checked live in ListMyRecords (403 RECORDS_VIEW_FORBIDDEN)."),
        new("GET /api/records/{recordType}/{id:publicid}", EndpointAccess.Authenticated, RecordsModule, "One record for the caller's audience; owner with the type's view-own key, staff with its queue key reaching the record's organization, or records.oversight.view, all checked live by RecordLoader; anyone else gets 404."),
        new("GET /api/records/{recordType}/{id:publicid}/timeline", EndpointAccess.Authenticated, RecordsModule, "The timeline of a record the caller may see (same live check as the detail; 404 otherwise)."),
        new("POST /api/records/{recordType}/{id:publicid}/transitions", EndpointAccess.Authenticated, RecordsModule, "Takes a workflow action; the permission is the transition rule's own key (catalog data), decided live by RecordWorkflowEngine.Decide after the visibility check (403 WORKFLOW_FORBIDDEN, 404 hidden)."),
        new("GET /api/service-requests/{id:publicid}", EndpointAccess.Authenticated, ServiceRequestsModule, "The sample's detail cards for whoever may see the record, decided live by the Records module (IRecordVisibility, the RecordLoader rule): the owner with service-requests.view-own; once submitted, a service-requests.queue or records.oversight.view holder whose scope reaches the record; anyone else gets 404."), // codelab:module ServiceRequests
        new("GET /api/features", EndpointAccess.Anonymous, EndpointOwners.Kernel, "Feature flags are read before sign-in; flags only, never identities."),
        new("POST /api/security/csp-reports", EndpointAccess.Anonymous, EndpointOwners.Kernel, "The CSP report sink: browsers post violation reports without the page's credentials or request proof, so an authorized sink would receive nothing. Rate-limited per client (Codelab client policy), body capped at 16 KiB, application/csp-report and application/reports+json only; logs redacted fields (no query strings, referrer or script sample) and stores nothing."),
        new("GET /api/ops/health", EndpointAccess.ServiceAssertion, OpsModule, "Ops Console API only: ES256 service assertion of the trusted caller (Codelab:Ops:TrustedCallerKeyId); no user credential opens it; operator recorded."), // codelab:module Ops
        new("GET /api/ops/jobs", EndpointAccess.ServiceAssertion, OpsModule, "Ops Console API only (service assertion); job catalog and newest runs."), // codelab:module Ops
        new("GET /api/ops/certificates", EndpointAccess.ServiceAssertion, OpsModule, "Ops Console API only (service assertion); certificate expiry."), // codelab:module Ops
        new("GET /api/ops/reference-codes/{code}", EndpointAccess.ServiceAssertion, OpsModule, "Ops Console API only (service assertion); reference-code lookup, codes not record ids."), // codelab:module Ops
        new("GET /api/security/session-signing-key", EndpointAccess.Authenticated, EndpointOwners.Platform, "Issues the caller's session signing key right after sign-in; Codelab.Security requires authentication."),
        new("* /health/live", EndpointAccess.Anonymous, EndpointOwners.Platform, "Liveness probe for the load balancer; runs no check."),
        new("* /health/ready", EndpointAccess.Anonymous, EndpointOwners.Platform, "Readiness probe for the load balancer."),
        new("GET /openapi/{documentName}.json", EndpointAccess.Anonymous, EndpointOwners.Kernel, "The OpenAPI document, mapped in Development only."),
    ];
}
