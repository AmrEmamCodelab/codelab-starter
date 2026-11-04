using System.Data;
using Codelab.Security.Storage;
using Microsoft.Data.SqlClient;

namespace Codelab.Security.Replay;

/// <summary>
/// The SQL Server replay guard, shared by every replica: a consume is one INSERT into
/// <c>dbo.SecurityReplayNonce</c> keyed by the nonce hash, so the primary key makes it atomic across processes.
/// Any store failure answers <see cref="NonceConsumeOutcome.StoreUnavailable"/> (the caller refuses 503), never Fresh.
/// </summary>
public sealed partial class SqlReplayGuard : IReplayGuard, IExpiredEntryPurger
{
    /// <summary>Rows deleted per purge statement.</summary>
    public const int PurgeBatchSize = 5_000;

    /// <summary>Most purge statements per call (the rest waits for the next run).</summary>
    public const int MaxPurgeBatches = 100;

    private const int HashBytes = 32;

    private const string ConsumeSql =
        "INSERT INTO " + SecuritySqlSchema.ReplayNonceTable + " (NonceHash, ExpiresAtUtc, ConsumedAtUtc) VALUES (@hash, @expires, @now);";

    private const string PurgeSql =
        "DELETE TOP (@batch) FROM " + SecuritySqlSchema.ReplayNonceTable + " WHERE ExpiresAtUtc <= @now;";

    private readonly SqlStoreOptions _options;
    private readonly TimeProvider _clock;
    private readonly ILogger<SqlReplayGuard> _logger;

    /// <summary>Creates the guard.</summary>
    public SqlReplayGuard(SqlStoreOptions options, TimeProvider clock, ILogger<SqlReplayGuard> logger)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentException.ThrowIfNullOrWhiteSpace(options.ConnectionString, nameof(options));
        ArgumentNullException.ThrowIfNull(clock);
        ArgumentNullException.ThrowIfNull(logger);
        _options = options;
        _clock = clock;
        _logger = logger;
    }

    /// <inheritdoc />
    public string StoreName => nameof(SqlReplayGuard);

    /// <inheritdoc />
    public async Task<NonceConsumeOutcome> TryConsumeAsync(ReplayNonce nonce, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(nonce);
        var now = _clock.GetUtcNow();
        if (nonce.ExpiresAt <= now)
        {
            return NonceConsumeOutcome.Replayed;
        }

        try
        {
            await InsertAsync(nonce, now, cancellationToken);
            return NonceConsumeOutcome.Fresh;
        }
        catch (SqlException ex) when (SqlErrorNumbers.IsDuplicateKey(ex))
        {
            return NonceConsumeOutcome.Replayed;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            LogStoreUnavailable(_logger, ex, StoreName);
            return NonceConsumeOutcome.StoreUnavailable;
        }
    }

    /// <inheritdoc />
    public async Task<int> PurgeExpiredAsync(CancellationToken cancellationToken)
    {
        await using var connection = await OpenAsync(cancellationToken);
        var total = 0;
        for (var batch = 0; batch < MaxPurgeBatches; batch++)
        {
            await using var command = Command(PurgeSql, connection);
            command.Parameters.Add("@batch", SqlDbType.Int).Value = PurgeBatchSize;
            command.Parameters.Add("@now", SqlDbType.DateTime2).Value = _clock.GetUtcNow().UtcDateTime;
            var deleted = await command.ExecuteNonQueryAsync(cancellationToken);
            total += deleted;
            if (deleted < PurgeBatchSize)
            {
                return total;
            }
        }

        LogPurgeCapped(_logger, StoreName, MaxPurgeBatches, total);
        return total;
    }

    private async Task InsertAsync(ReplayNonce nonce, DateTimeOffset now, CancellationToken cancellationToken)
    {
        await using var connection = await OpenAsync(cancellationToken);
        await using var command = Command(ConsumeSql, connection);
        command.Parameters.Add("@hash", SqlDbType.Binary, HashBytes).Value = nonce.KeyHash();
        command.Parameters.Add("@expires", SqlDbType.DateTime2).Value = nonce.ExpiresAt.UtcDateTime;
        command.Parameters.Add("@now", SqlDbType.DateTime2).Value = now.UtcDateTime;
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private async Task<SqlConnection> OpenAsync(CancellationToken cancellationToken)
    {
        var connection = new SqlConnection(_options.ConnectionString);
        await connection.OpenAsync(cancellationToken);
        return connection;
    }

    private SqlCommand Command(string sql, SqlConnection connection) =>
        new(sql, connection) { CommandTimeout = _options.CommandTimeoutSeconds };

    [LoggerMessage(EventId = SecurityLogEvents.StoreUnavailable, Level = LogLevel.Error, Message = "The {Store} could not answer; the caller fails closed until it recovers.")]
    private static partial void LogStoreUnavailable(ILogger logger, Exception exception, string store);

    [LoggerMessage(EventId = SecurityLogEvents.PurgeCapped, Level = LogLevel.Warning, Message = "The {Store} purge stopped at its {MaxBatches}-batch cap after deleting {Total} rows; expired rows remain.")]
    private static partial void LogPurgeCapped(ILogger logger, string store, int maxBatches, int total);
}
