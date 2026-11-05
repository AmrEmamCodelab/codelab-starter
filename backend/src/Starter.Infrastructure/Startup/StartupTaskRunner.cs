using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Starter.Infrastructure.Persistence;

namespace Starter.Infrastructure.Startup;

/// <summary>
/// Runs once before the host accepts requests: applies every module's migrations when
/// <see cref="DatabaseOptions.ApplyMigrationsOnStartup"/> is on, then every <see cref="IStartupTask"/> in phase order.
/// A failure stops the host (the exception propagates out of <see cref="StartAsync"/>).
/// </summary>
public sealed partial class StartupTaskRunner(
    IServiceScopeFactory scopes,
    IOptions<DatabaseOptions> database,
    ILogger<StartupTaskRunner> logger) : IHostedService
{
    /// <inheritdoc />
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        await using var scope = scopes.CreateAsyncScope();
        if (database.Value.ApplyMigrationsOnStartup)
        {
            await MigrateAsync(scope.ServiceProvider, cancellationToken);
        }

        var tasks = scope.ServiceProvider.GetServices<IStartupTask>().OrderBy(task => task.Phase);
        foreach (var task in tasks)
        {
            LogTaskStarting(logger, task.GetType().Name, task.Phase);
            await task.RunAsync(cancellationToken);
        }
    }

    /// <inheritdoc />
    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    private async Task MigrateAsync(IServiceProvider services, CancellationToken cancellationToken)
    {
        foreach (var moduleDatabase in services.GetServices<IModuleDatabase>())
        {
            LogMigrating(logger, moduleDatabase.Name);
            await moduleDatabase.MigrateAsync(cancellationToken);
        }
    }

    [LoggerMessage(EventId = 2001, Level = LogLevel.Information, Message = "Applying pending migrations of {DbContext}")]
    private static partial void LogMigrating(ILogger logger, string dbContext);

    [LoggerMessage(EventId = 2002, Level = LogLevel.Information, Message = "Running startup task {Task} ({Phase})")]
    private static partial void LogTaskStarting(ILogger logger, string task, StartupPhase phase);
}
