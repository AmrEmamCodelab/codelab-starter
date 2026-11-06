using Starter.Infrastructure.Hosting;

namespace Starter.Modules.Jobs.Api;

/// <summary>The jobs routes, built from the kernel prefix (no route literal elsewhere).</summary>
public static class JobsRoutes
{
    /// <summary>The admin jobs: <c>api/admin/jobs</c>.</summary>
    public const string Admin = ApiRoutes.Prefix + ApiRoutes.Separator + "admin" + ApiRoutes.Separator + "jobs";

    /// <summary>The name of the route parameter that carries a job key (a stable key, not a record id).</summary>
    public const string KeyParameter = "key";

    /// <summary>The runs of one job: <c>{key}/runs</c>.</summary>
    public const string Runs = "{" + KeyParameter + "}" + ApiRoutes.Separator + "runs";

    /// <summary>The response content type.</summary>
    public const string JsonContentType = "application/json";
}
