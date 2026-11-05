using Microsoft.Extensions.Logging;
using Starter.Infrastructure.Startup;
using Starter.Modules.Access.Application;

namespace Starter.Modules.Access.Infrastructure;

/// <summary>
/// Writes the permission catalog to the database on every start, in the <see cref="StartupPhase.ReferenceData"/>
/// phase, so account seeding (<see cref="StartupPhase.Accounts"/>) can grant keys afterwards. Idempotent.
/// </summary>
public sealed partial class PermissionCatalogStartupTask(
    PermissionSynchronizer synchronizer,
    ILogger<PermissionCatalogStartupTask> logger) : IStartupTask
{
    /// <inheritdoc />
    public StartupPhase Phase => StartupPhase.ReferenceData;

    /// <inheritdoc />
    public async Task RunAsync(CancellationToken cancellationToken)
    {
        var report = await synchronizer.SynchronizeAsync(cancellationToken);
        LogSynchronized(logger, report.Added, report.Refreshed, report.Retired);
    }

    [LoggerMessage(EventId = 3101, Level = LogLevel.Information, Message = "Permission catalog synchronized: {Added} added, {Refreshed} refreshed, {Retired} retired")]
    private static partial void LogSynchronized(ILogger logger, int added, int refreshed, int retired);
}
