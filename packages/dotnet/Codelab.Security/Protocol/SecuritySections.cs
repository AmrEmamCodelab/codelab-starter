namespace Codelab.Security;

/// <summary>The configuration sections the package binds. Every options type names its section from here.</summary>
public static class SecuritySections
{
    /// <summary>The root of every Codelab.Security setting.</summary>
    public const string Root = "Codelab:Security";

    /// <summary><see cref="SecurityProtocolOptions"/>.</summary>
    public const string Protocol = Root + ":Protocol";

    /// <summary>Key-ring guard switches (<c>Keys.KeyGuardOptions</c>).</summary>
    public const string KeyGuards = Root + ":KeyGuards";

    /// <summary>The PublicId key ring.</summary>
    public const string PublicIds = Root + ":PublicIds";

    /// <summary>Request signing (mode, audience, master key ring).</summary>
    public const string RequestSigning = Root + ":RequestSigning";

    /// <summary>Response signing (mode).</summary>
    public const string ResponseSigning = Root + ":ResponseSigning";

    /// <summary>Replay protection (store, retention).</summary>
    public const string ReplayProtection = Root + ":ReplayProtection";

    /// <summary>The SQL Server connection shared by the SQL-backed stores.</summary>
    public const string SqlStore = Root + ":SqlStore";

    /// <summary>Session claims and revocation.</summary>
    public const string Sessions = Root + ":Sessions";

    /// <summary>Security response headers and HSTS.</summary>
    public const string Headers = Root + ":Headers";

    /// <summary>CORS origins.</summary>
    public const string Cors = Root + ":Cors";

    /// <summary>Client rate limiting and the trusted-proxy list.</summary>
    public const string RateLimiting = Root + ":RateLimiting";

    /// <summary>Sealed one-time tickets.</summary>
    public const string Tickets = Root + ":Tickets";

    /// <summary>Upload limits and receipts.</summary>
    public const string Uploads = Root + ":Uploads";

    /// <summary>Service-to-service assertions issued by this host.</summary>
    public const string ServiceAssertionClient = Root + ":ServiceAssertions:Client";

    /// <summary>Service-to-service assertions accepted by this host.</summary>
    public const string ServiceAssertionServer = Root + ":ServiceAssertions:Server";
}
