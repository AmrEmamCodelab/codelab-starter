using Starter.Infrastructure.Hosting;

namespace Starter.Modules.Ops.Api;

/// <summary>The Ops routes, built from the kernel prefix (no route literal elsewhere).</summary>
public static class OpsRoutes
{
    /// <summary>The root of every Ops endpoint: <c>api/ops</c>.</summary>
    public const string Root = ApiRoutes.Prefix + ApiRoutes.Separator + "ops";

    /// <summary>The health summary: <c>api/ops/health</c>.</summary>
    public const string Health = "health";

    /// <summary>The job catalog with the newest runs: <c>api/ops/jobs</c>.</summary>
    public const string Jobs = "jobs";

    /// <summary>The certificate expiry list: <c>api/ops/certificates</c>.</summary>
    public const string Certificates = "certificates";

    /// <summary>The name of the route parameter that carries a reference code (a code, not a record id).</summary>
    public const string CodeParameter = "code";

    /// <summary>One reference code: <c>api/ops/reference-codes/{code}</c>.</summary>
    public const string ReferenceCode = "reference-codes" + ApiRoutes.Separator + "{" + CodeParameter + "}";

    /// <summary>The response content type.</summary>
    public const string JsonContentType = "application/json";
}

/// <summary>The request headers of the Ops surface.</summary>
public static class OpsHeaders
{
    /// <summary>
    /// The operator the Ops Console API acts for (its Windows account), sent with every call next to the service
    /// assertion and recorded in <c>ops.OperatorAccess</c>. Codelab.Security binds the assertion to the method, path
    /// and body, not to this header: the name is attributed to the trusted caller, which is its authority.
    /// </summary>
    public const string Operator = "X-Codelab-Operator";
}
