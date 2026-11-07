namespace Codelab.OpsConsole.Api.Upstream;

/// <summary>The starter API's <c>/api/ops</c> routes, relative to <c>Codelab:OpsConsole:Api:BaseUrl</c>.</summary>
public static class OpsApiRoutes
{
    /// <summary>The ops surface root.</summary>
    public const string Root = "api/ops";

    /// <summary>GET: the API's health report.</summary>
    public const string Health = Root + "/health";

    /// <summary>GET: background job status.</summary>
    public const string Jobs = Root + "/jobs";

    /// <summary>GET: watched certificates and their expiry verdicts.</summary>
    public const string Certificates = Root + "/certificates";

    /// <summary>GET <c>api/ops/reference-codes/{code}</c>: what the API knows about one reference code.</summary>
    public const string ReferenceCodes = Root + "/reference-codes";

    /// <summary>The route of one reference code (already normalized, so it needs no escaping beyond the defensive one).</summary>
    public static string ReferenceCode(string referenceCode)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(referenceCode);
        return ReferenceCodes + "/" + Uri.EscapeDataString(referenceCode);
    }
}
