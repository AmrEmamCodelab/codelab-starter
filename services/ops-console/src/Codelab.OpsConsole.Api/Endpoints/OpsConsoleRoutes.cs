namespace Codelab.OpsConsole.Api.Endpoints;

/// <summary>The console's backend-for-frontend routes, the only API the ops web calls (the dev server proxies /ops-api to :5188).</summary>
public static class OpsConsoleRoutes
{
    /// <summary>The prefix of every console route.</summary>
    public const string Prefix = "/ops-api";

    /// <summary>GET: the signed-in operator.</summary>
    public const string Me = "/me";

    /// <summary>GET: the API's health report.</summary>
    public const string Health = "/health";

    /// <summary>GET: background job status.</summary>
    public const string Jobs = "/jobs";

    /// <summary>GET: certificate expiry.</summary>
    public const string Certificates = "/certificates";

    /// <summary>GET: one reference code traced through the API and Seq.</summary>
    public const string ReferenceCode = "/reference-codes/{code}";
}
