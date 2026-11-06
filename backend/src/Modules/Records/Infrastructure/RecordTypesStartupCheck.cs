using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Starter.Modules.Records.Application;

namespace Starter.Modules.Records.Infrastructure;

/// <summary>
/// Builds <see cref="RecordTypes"/> in the host's <c>StartingAsync</c> phase, before any hosted service (migrations,
/// jobs, Kestrel) starts, so a record type registered twice, without a workflow, or whose workflow has no initial
/// status or no submit rule out of it stops the boot with every problem listed. It needs no database.
/// </summary>
public sealed class RecordTypesStartupCheck(IServiceProvider services) : IHostedLifecycleService
{
    /// <inheritdoc />
    public Task StartingAsync(CancellationToken cancellationToken)
    {
        _ = services.GetRequiredService<RecordTypes>();
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
