using Hangfire;
using Hangfire.SqlServer;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Starter.Infrastructure.Persistence;
using Starter.Modules.Jobs.Infrastructure.Options;

namespace Starter.Modules.Jobs.Infrastructure.Scheduling;

/// <summary>
/// Installs or upgrades the scheduler's tables in schema <see cref="SchedulerRegistration.SchemaName"/> once the
/// application database exists, on a job-server node (<c>Jobs:Enabled</c>). Such a node resolves the storage while the
/// host is being built, before the migration startup tasks may have created a new database; the storage's own attempt
/// failed there on a fresh database (and poisoned the connection pool for the next start-up step), so the node stopped
/// on <c>Invalid object name 'hangfire.Hash'</c>. Its storage is therefore created with schema preparation off, and
/// this hosted service, registered after the kernel's migration startup tasks and before the
/// <see cref="RecurringJobRegistrar"/> and the job server, installs the schema instead. Other nodes first resolve the
/// storage on a request, after start-up, and let it prepare the schema itself.
/// </summary>
/// <remarks>
/// Idempotent and safe on many nodes at once: the installer serializes itself with an application lock and does
/// nothing when the schema is current. Skipped when <c>Jobs:PrepareSchemaIfNecessary</c> is false (the DBA installs
/// the schema) and for any other storage (the tests' in-memory storage).
/// </remarks>
public sealed class SchedulerSchema(JobStorage storage, IOptions<ConnectionStringsOptions> connectionStrings, IOptions<JobsOptions> options)
    : IHostedService
{
    /// <inheritdoc />
    public Task StartAsync(CancellationToken cancellationToken)
    {
        EnsureInstalled();
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    /// <summary>Installs or upgrades the schema; false when this node must not (see the remarks).</summary>
    public bool EnsureInstalled()
    {
        if (storage is not SqlServerStorage || !options.Value.PrepareSchemaIfNecessary)
        {
            return false;
        }

        using var connection = new SqlConnection(connectionStrings.Value.Starter);
        connection.Open();
        SqlServerObjectsInstaller.Install(connection, SchedulerRegistration.SchemaName);
        return true;
    }
}
