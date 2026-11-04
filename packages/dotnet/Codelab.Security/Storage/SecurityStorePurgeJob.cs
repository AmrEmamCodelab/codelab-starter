using Codelab.Security.Replay;
using Codelab.Security.Sessions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace Codelab.Security.Storage;

/// <summary>
/// The purge job hook: runs every registered <see cref="IExpiredEntryPurger"/> (replay nonces, revoked sessions) and
/// reports how many rows each removed. The package schedules nothing; the host's job module (P5) calls
/// <see cref="RunAsync"/> on its own schedule. A failing store does not stop the others; its failure is rethrown
/// after every store ran, so the scheduler records and retries the run.
/// </summary>
public sealed class SecurityStorePurgeJob(IEnumerable<IExpiredEntryPurger> purgers)
{
    /// <summary>The job key for job catalogs.</summary>
    public const string JobKey = "codelab-security-store-purge";

    /// <summary>Purges every store; returns the removed count per store name.</summary>
    /// <exception cref="AggregateException">One or more stores failed (the others still ran).</exception>
    public async Task<IReadOnlyDictionary<string, int>> RunAsync(CancellationToken cancellationToken)
    {
        var removed = new Dictionary<string, int>(StringComparer.Ordinal);
        var failures = new List<Exception>();
        foreach (var purger in purgers)
        {
            try
            {
                removed[purger.StoreName] = await purger.PurgeExpiredAsync(cancellationToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                failures.Add(ex);
            }
        }

        return failures.Count == 0 ? removed : throw new AggregateException("One or more security stores failed to purge.", failures);
    }
}

/// <summary>Creates the SQL store tables at start when a store uses SQL Server and EnsureSchemaOnStart is on.</summary>
internal sealed class SqlSchemaStartup(
    IOptions<SqlStoreOptions> sqlOptions,
    IOptions<ReplayProtectionOptions> replayOptions,
    IOptions<SecuritySessionOptions> sessionOptions,
    ILogger<SqlSchemaStartup> logger) : IHostedService
{
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        var sqlInUse = replayOptions.Value.Store is SecurityStoreKind.SqlServer || sessionOptions.Value.Store is SecurityStoreKind.SqlServer;
        if (!sqlInUse || !sqlOptions.Value.EnsureSchemaOnStart)
        {
            return;
        }

        await SecuritySqlSchema.EnsureCreatedAsync(sqlOptions.Value.ConnectionString!, cancellationToken);
        SecurityLog.SchemaEnsured(logger);
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
