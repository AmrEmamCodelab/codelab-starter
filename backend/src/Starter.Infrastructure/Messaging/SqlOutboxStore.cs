using System.Data;
using System.Data.Common;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Starter.Application.Messaging;

namespace Starter.Infrastructure.Messaging;

/// <summary>
/// The <see cref="IOutboxStore"/> on SQL Server, in raw SQL over <see cref="OutboxDbContext"/>'s connection: the claim's
/// correctness rests on one atomic statement, which a change tracker would only obscure.
/// </summary>
/// <remarks>
/// The claim reads with <c>READPAST, UPDLOCK, ROWLOCK</c>: a concurrent drain skips rows this one holds instead of
/// waiting, the update lock is taken at read time so two claims cannot both qualify a row, and row locks keep one drain
/// from taking the whole backlog through escalation. Every outcome update is guarded by status (and by attempt for
/// retry and failure), so a drain whose lease expired cannot overwrite the drain that reclaimed the row.
/// </remarks>
public sealed class SqlOutboxStore(OutboxDbContext context) : IOutboxStore
{
    private const string Table = $"[{OutboxDbContext.SchemaName}].[{OutboxTableNames.OutboxMessages}]";

    private const string FailAbandonedSql = $"""
        UPDATE {Table} WITH (READPAST, ROWLOCK)
        SET Status = @failed, LockedUntil = NULL, LastError = @error
        WHERE Status = @processing AND LockedUntil <= @now AND Attempt >= @maxAttempts;
        """;

    private const string ClaimSql = $"""
        WITH due AS (
            SELECT TOP (@batchSize) Id, Type, PayloadJson, Status, Attempt, LockedUntil
            FROM {Table} WITH (READPAST, UPDLOCK, ROWLOCK)
            WHERE (Status = @pending AND (NotBefore IS NULL OR NotBefore <= @now))
               OR (Status = @processing AND LockedUntil <= @now AND Attempt < @maxAttempts)
            ORDER BY Id)
        UPDATE due
        SET Status = @processing, Attempt = Attempt + 1, LockedUntil = @lockedUntil
        OUTPUT inserted.Id, inserted.Type, inserted.PayloadJson, inserted.Attempt;
        """;

    private const string MarkSentSql = $"""
        UPDATE {Table}
        SET Status = @sent, SentAt = @now, LockedUntil = NULL, LastError = NULL
        WHERE Id = @id AND Status = @processing;
        """;

    private const string MarkRetrySql = $"""
        UPDATE {Table}
        SET Status = @pending, NotBefore = @notBefore, LockedUntil = NULL, LastError = @error
        WHERE Id = @id AND Status = @processing AND Attempt = @attempt;
        """;

    private const string MarkFailedSql = $"""
        UPDATE {Table}
        SET Status = @failed, LockedUntil = NULL, LastError = @error
        WHERE Id = @id AND Status = @processing AND Attempt = @attempt;
        """;

    private const int IdOrdinal = 0;
    private const int TypeOrdinal = 1;
    private const int PayloadOrdinal = 2;
    private const int AttemptOrdinal = 3;

    /// <inheritdoc />
    public Task<int> FailAbandonedAsync(DateTimeOffset now, CancellationToken cancellationToken) =>
        ExecuteAsync(
            FailAbandonedSql,
            [
                Status(OutboxSqlParameters.Failed, OutboxMessageStatus.Failed),
                Status(OutboxSqlParameters.Processing, OutboxMessageStatus.Processing),
                Time(OutboxSqlParameters.Now, now),
                Int(OutboxSqlParameters.MaxAttempts, OutboxLimits.MaxAttempts),
                Error(OutboxErrorCodes.LeaseExpired),
            ],
            command => command.ExecuteNonQueryAsync(cancellationToken),
            cancellationToken);

    /// <inheritdoc />
    public Task<IReadOnlyList<OutboxDelivery>> ClaimAsync(OutboxClaim claim, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(claim);
        return ExecuteAsync(
            ClaimSql,
            [
                Int(OutboxSqlParameters.BatchSize, claim.BatchSize),
                Status(OutboxSqlParameters.Pending, OutboxMessageStatus.Pending),
                Status(OutboxSqlParameters.Processing, OutboxMessageStatus.Processing),
                Time(OutboxSqlParameters.Now, claim.Now),
                Time(OutboxSqlParameters.LockedUntil, claim.LockedUntil),
                Int(OutboxSqlParameters.MaxAttempts, OutboxLimits.MaxAttempts),
            ],
            command => ReadClaimsAsync(command, cancellationToken),
            cancellationToken);
    }

    /// <inheritdoc />
    public Task MarkSentAsync(long messageId, DateTimeOffset sentAt, CancellationToken cancellationToken) =>
        ExecuteAsync(
            MarkSentSql,
            [
                Status(OutboxSqlParameters.Sent, OutboxMessageStatus.Sent),
                Status(OutboxSqlParameters.Processing, OutboxMessageStatus.Processing),
                Time(OutboxSqlParameters.Now, sentAt),
                Id(messageId),
            ],
            command => command.ExecuteNonQueryAsync(cancellationToken),
            cancellationToken);

    /// <inheritdoc />
    public Task MarkRetryAsync(OutboxRetry retry, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(retry);
        return ExecuteAsync(
            MarkRetrySql,
            [
                Status(OutboxSqlParameters.Pending, OutboxMessageStatus.Pending),
                Status(OutboxSqlParameters.Processing, OutboxMessageStatus.Processing),
                Time(OutboxSqlParameters.NotBefore, retry.NotBefore),
                Error(retry.ErrorCode),
                Id(retry.MessageId),
                Int(OutboxSqlParameters.Attempt, retry.Attempt),
            ],
            command => command.ExecuteNonQueryAsync(cancellationToken),
            cancellationToken);
    }

    /// <inheritdoc />
    public Task MarkFailedAsync(OutboxAttemptFailure failure, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(failure);
        return ExecuteAsync(
            MarkFailedSql,
            [
                Status(OutboxSqlParameters.Failed, OutboxMessageStatus.Failed),
                Status(OutboxSqlParameters.Processing, OutboxMessageStatus.Processing),
                Error(failure.ErrorCode),
                Id(failure.MessageId),
                Int(OutboxSqlParameters.Attempt, failure.Attempt),
            ],
            command => command.ExecuteNonQueryAsync(cancellationToken),
            cancellationToken);
    }

    /// <summary>The stored error: the code only, cut to the column so a long code never loses the record.</summary>
    public static string ToStoredError(string errorCode)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(errorCode);
        return errorCode.Length <= OutboxColumnLimits.LastErrorMaxLength
            ? errorCode
            : errorCode[..OutboxColumnLimits.LastErrorMaxLength];
    }

    private static async Task<IReadOnlyList<OutboxDelivery>> ReadClaimsAsync(DbCommand command, CancellationToken cancellationToken)
    {
        var claims = new List<OutboxDelivery>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            claims.Add(new OutboxDelivery(
                MessageId: reader.GetInt64(IdOrdinal),
                Type: reader.GetString(TypeOrdinal),
                PayloadJson: reader.GetString(PayloadOrdinal),
                Attempt: reader.GetInt32(AttemptOrdinal)));
        }

        claims.Sort((left, right) => left.MessageId.CompareTo(right.MessageId));
        return claims;
    }

    /// <summary>Runs one command on the context's connection (and its transaction, when one is open).</summary>
    private async Task<T> ExecuteAsync<T>(
        string sql, SqlParameter[] parameters, Func<DbCommand, Task<T>> run, CancellationToken cancellationToken)
    {
        var connection = context.Database.GetDbConnection();
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.Transaction = context.Database.CurrentTransaction?.GetDbTransaction();
        command.Parameters.AddRange(parameters);
        var openedHere = connection.State != ConnectionState.Open;
        if (openedHere)
        {
            await connection.OpenAsync(cancellationToken);
        }

        try
        {
            return await run(command);
        }
        finally
        {
            if (openedHere)
            {
                await connection.CloseAsync();
            }
        }
    }

    private static SqlParameter Status(string name, OutboxMessageStatus status) => Int(name, (int)status);

    private static SqlParameter Int(string name, int value) => new(name, SqlDbType.Int) { Value = value };

    private static SqlParameter Id(long messageId) => new(OutboxSqlParameters.Id, SqlDbType.BigInt) { Value = messageId };

    private static SqlParameter Time(string name, DateTimeOffset value) => new(name, SqlDbType.DateTimeOffset) { Value = value };

    private static SqlParameter Error(string errorCode) =>
        new(OutboxSqlParameters.Error, SqlDbType.NVarChar, OutboxColumnLimits.LastErrorMaxLength) { Value = ToStoredError(errorCode) };
}

/// <summary>The parameter names of the outbox SQL.</summary>
internal static class OutboxSqlParameters
{
    public const string Id = "@id";
    public const string Attempt = "@attempt";
    public const string BatchSize = "@batchSize";
    public const string MaxAttempts = "@maxAttempts";
    public const string Now = "@now";
    public const string LockedUntil = "@lockedUntil";
    public const string NotBefore = "@notBefore";
    public const string Error = "@error";
    public const string Pending = "@pending";
    public const string Sent = "@sent";
    public const string Failed = "@failed";
    public const string Processing = "@processing";
}
