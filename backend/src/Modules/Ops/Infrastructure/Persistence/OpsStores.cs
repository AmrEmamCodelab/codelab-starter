using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Starter.Modules.Ops.Application;
using Starter.Modules.Ops.Domain;

namespace Starter.Modules.Ops.Infrastructure.Persistence;

/// <summary><see cref="IReferenceCodeSightings"/> on <see cref="OpsDbContext"/>.</summary>
public sealed class SqlReferenceCodeSightings(OpsDbContext context) : IReferenceCodeSightings
{
    /// <inheritdoc />
    public Task<ReferenceCodeSighting?> FindNewestAsync(string referenceCode, CancellationToken cancellationToken) =>
        context.ReferenceCodeSightings.AsNoTracking()
            .Where(sighting => sighting.ReferenceCode == referenceCode)
            .OrderByDescending(sighting => sighting.Id)
            .FirstOrDefaultAsync(cancellationToken);

    /// <inheritdoc />
    public async Task AddAsync(IReadOnlyCollection<ReferenceCodeSighting> sightings, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(sightings);
        context.ReferenceCodeSightings.AddRange(sightings);
        await context.SaveChangesAsync(cancellationToken);
        context.ChangeTracker.Clear();
    }

    /// <inheritdoc />
    public Task<int> PurgeOlderThanAsync(DateTimeOffset cutoff, CancellationToken cancellationToken) =>
        context.ReferenceCodeSightings.Where(sighting => sighting.OccurredAt < cutoff).ExecuteDeleteAsync(cancellationToken);
}

/// <summary>
/// <see cref="IOperatorAccessLog"/> on <see cref="OpsDbContext"/>, best effort by the port's contract: a failed write is
/// logged with the endpoint and the correlation id (never the operator name) and the call goes on, so the read-only
/// Ops endpoints still answer while the database is down (the health summary then shows the database unhealthy).
/// </summary>
public sealed partial class SqlOperatorAccessLog(OpsDbContext context, ILogger<SqlOperatorAccessLog> logger) : IOperatorAccessLog
{
    /// <inheritdoc />
    public async Task RecordAsync(OperatorAccess access, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(access);
        context.OperatorAccess.Add(access);
        try
        {
            await context.SaveChangesAsync(cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Load-bearing: the audit row is a side record of a read-only call; the call itself must still answer.
            context.ChangeTracker.Clear();
            NotRecorded(logger, ex, access.Endpoint, access.CorrelationId);
        }
    }

    [LoggerMessage(EventName = "OpsOperatorAccessNotRecorded", Level = LogLevel.Error, Message = "Ops operator access not recorded {Endpoint} {CorrelationId}")]
    private static partial void NotRecorded(ILogger logger, Exception exception, OpsEndpoint endpoint, string? correlationId);
}
