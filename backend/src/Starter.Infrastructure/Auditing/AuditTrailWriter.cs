using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace Starter.Infrastructure.Auditing;

/// <summary>
/// Writes audit rows next to the save of a module context, so they commit or roll back with it. Begun before the
/// save (it may open the transaction the save then joins), written and committed after it, disposed on every path.
/// </summary>
public interface IAuditTrailWriter
{
    /// <summary>Begins the write of one save of <paramref name="source"/>.</summary>
    IAuditWriteSession Begin(DbContext source);

    /// <summary>Begins the write of one save of <paramref name="source"/>.</summary>
    Task<IAuditWriteSession> BeginAsync(DbContext source, CancellationToken cancellationToken);
}

/// <summary>The audit write of one save. Disposing it without <see cref="Commit"/> rolls back what it opened.</summary>
public interface IAuditWriteSession : IDisposable, IAsyncDisposable
{
    /// <summary>Writes <paramref name="rows"/> and commits the transaction the session opened (if it opened one).</summary>
    void Commit(IReadOnlyList<AuditEntry> rows);

    /// <summary>Writes <paramref name="rows"/> and commits the transaction the session opened (if it opened one).</summary>
    Task CommitAsync(IReadOnlyList<AuditEntry> rows, CancellationToken cancellationToken);
}

/// <summary>
/// The <see cref="IAuditTrailWriter"/> on SQL Server. When the module context has no transaction of its own (and there
/// is no ambient <see cref="System.Transactions.Transaction"/>), the session opens one before the save, so the change
/// and its audit rows are one unit; otherwise both join the caller's transaction and the caller commits. The rows are
/// written through a short-lived <see cref="AuditDbContext"/> on the module context's connection.
/// </summary>
/// <remarks>
/// A module context must not enable a retrying execution strategy: SQL Server's refuses a transaction it did not open.
/// </remarks>
public sealed class SqlAuditTrailWriter : IAuditTrailWriter
{
    /// <inheritdoc />
    public IAuditWriteSession Begin(DbContext source)
    {
        ArgumentNullException.ThrowIfNull(source);
        var owned = NeedsOwnTransaction(source) ? source.Database.BeginTransaction() : null;
        return new Session(source, owned);
    }

    /// <inheritdoc />
    public async Task<IAuditWriteSession> BeginAsync(DbContext source, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(source);
        var owned = NeedsOwnTransaction(source) ? await source.Database.BeginTransactionAsync(cancellationToken) : null;
        return new Session(source, owned);
    }

    private static bool NeedsOwnTransaction(DbContext source) =>
        source.Database.CurrentTransaction is null && System.Transactions.Transaction.Current is null;

    private sealed class Session(DbContext source, IDbContextTransaction? owned) : IAuditWriteSession
    {
        public void Commit(IReadOnlyList<AuditEntry> rows)
        {
            ArgumentNullException.ThrowIfNull(rows);
            if (rows.Count > 0)
            {
                using var audit = new AuditDbContext(AuditDbContext.OptionsOn(source.Database.GetDbConnection()));
                JoinTransaction(audit);
                audit.Entries.AddRange(rows);
                audit.SaveChanges();
            }

            owned?.Commit();
        }

        public async Task CommitAsync(IReadOnlyList<AuditEntry> rows, CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(rows);
            if (rows.Count > 0)
            {
                await using var audit = new AuditDbContext(AuditDbContext.OptionsOn(source.Database.GetDbConnection()));
                JoinTransaction(audit);
                audit.Entries.AddRange(rows);
                await audit.SaveChangesAsync(cancellationToken);
            }

            if (owned is not null)
            {
                await owned.CommitAsync(cancellationToken);
            }
        }

        public void Dispose() => owned?.Dispose();

        public ValueTask DisposeAsync() => owned?.DisposeAsync() ?? ValueTask.CompletedTask;

        private void JoinTransaction(AuditDbContext audit)
        {
            if (source.Database.CurrentTransaction is { } transaction)
            {
                audit.Database.UseTransaction(transaction.GetDbTransaction());
            }
        }
    }
}
