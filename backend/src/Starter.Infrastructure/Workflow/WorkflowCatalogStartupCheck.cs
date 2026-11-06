using Microsoft.Extensions.Hosting;
using Starter.Application.Authorization;
using Starter.Application.Workflow;

namespace Starter.Infrastructure.Workflow;

/// <summary>
/// Validates every registered workflow definition against the permission catalog (the union of every
/// <see cref="IPermissionCatalogSource"/>) in <see cref="StartingAsync"/>, which the host runs before ANY hosted
/// service starts (migrations, jobs, the web server). Any violation throws <see cref="WorkflowCatalogInvalidException"/>
/// and the host does not start. No database is touched.
/// </summary>
public sealed class WorkflowCatalogStartupCheck(
    IEnumerable<IRecordWorkflowDefinition> definitions,
    IEnumerable<IPermissionCatalogSource> permissionSources) : IHostedLifecycleService
{
    /// <summary>Runs the check now (also used by tests and tools).</summary>
    /// <exception cref="WorkflowCatalogInvalidException">A catalog is invalid.</exception>
    public void Check()
    {
        var knownKeys = permissionSources
            .SelectMany(source => source.Permissions)
            .Select(permission => permission.Key)
            .ToHashSet(StringComparer.Ordinal);
        var violations = RecordWorkflowValidation.ValidateAll(definitions, knownKeys);
        if (violations.Count > 0)
        {
            throw new WorkflowCatalogInvalidException(violations);
        }
    }

    /// <inheritdoc />
    public Task StartingAsync(CancellationToken cancellationToken)
    {
        Check();
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task StartAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    /// <inheritdoc />
    public Task StartedAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    /// <inheritdoc />
    public Task StoppingAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    /// <inheritdoc />
    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    /// <inheritdoc />
    public Task StoppedAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
