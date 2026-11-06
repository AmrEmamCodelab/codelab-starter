using Codelab.Attachments.Core.Storage;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;

namespace Codelab.Attachments.Api.Hosting;

/// <summary>
/// Readiness: the storage root (<c>Attachments:Storage:RootPath</c>) is reachable. An unreachable share makes the
/// node not ready instead of failing every upload. The description never names the path.
/// </summary>
internal sealed class StorageReadinessCheck(IOptions<AttachmentStorageOptions> options) : IHealthCheck
{
    internal const string Unreachable = "The attachment storage root is not reachable.";

    public Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        var root = options.Value.RootPath;
        var reachable = !string.IsNullOrWhiteSpace(root) && Directory.Exists(root);
        return Task.FromResult(reachable ? HealthCheckResult.Healthy() : HealthCheckResult.Unhealthy(Unreachable));
    }
}
