using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Starter.Modules.Assignments.Application;
using Starter.Modules.Assignments.Domain;

namespace Starter.Modules.Assignments.Infrastructure.Persistence;

/// <summary>The EF Core <see cref="IAssignmentStore"/> over <see cref="AssignmentsDbContext"/>.</summary>
public sealed class AssignmentStore(AssignmentsDbContext context) : IAssignmentStore
{
    /// <summary>SQL Server's error number for a duplicate key in a unique index.</summary>
    private const int UniqueIndexViolation = 2601;

    /// <summary>SQL Server's error number for a duplicate key in a unique constraint.</summary>
    private const int UniqueConstraintViolation = 2627;

    /// <inheritdoc />
    public Task<RecordAssignment?> FindOpenAsync(long recordId, CancellationToken cancellationToken) =>
        context.RecordAssignments.FirstOrDefaultAsync(row => row.RecordId == recordId && row.ReleasedAt == null, cancellationToken);

    /// <inheritdoc />
    public async Task<IReadOnlyList<RecordAssignment>> ListOpenAsync(IReadOnlyCollection<long> recordIds, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(recordIds);
        return recordIds.Count == 0
            ? []
            : await context.RecordAssignments.AsNoTracking()
                .Where(row => recordIds.Contains(row.RecordId) && row.ReleasedAt == null)
                .ToListAsync(cancellationToken);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<long>> OpenRecordIdsAsync(OpenAssignmentFilter filter, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(filter);
        var open = context.RecordAssignments.AsNoTracking()
            .Where(row => row.RecordType == filter.RecordType && row.ReleasedAt == null);
        if (filter.AssigneeUserId is { } assignee)
        {
            open = open.Where(row => row.AssigneeUserId == assignee);
        }

        return await open.Select(row => row.RecordId).ToListAsync(cancellationToken);
    }

    /// <inheritdoc />
    public void Add(RecordAssignment assignment) => context.RecordAssignments.Add(assignment);

    /// <inheritdoc />
    public Task<int> SaveChangesAsync(CancellationToken cancellationToken) => context.SaveChangesAsync(cancellationToken);

    /// <inheritdoc />
    public async Task<bool> TrySaveAsync(CancellationToken cancellationToken)
    {
        try
        {
            await context.SaveChangesAsync(cancellationToken);
            return true;
        }
        catch (DbUpdateException exception) when (IsDuplicateKey(exception))
        {
            context.ChangeTracker.Clear();
            return false;
        }
    }

    /// <inheritdoc />
    public async Task<bool> TryReassignAsync(RecordAssignment replacement, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(replacement);
        await using var transaction = await context.Database.BeginTransactionAsync(cancellationToken);
        await context.SaveChangesAsync(cancellationToken);
        context.RecordAssignments.Add(replacement);
        if (!await TrySaveAsync(cancellationToken))
        {
            await transaction.RollbackAsync(cancellationToken);
            return false;
        }

        await transaction.CommitAsync(cancellationToken);
        return true;
    }

    private static bool IsDuplicateKey(DbUpdateException exception) =>
        exception.InnerException is SqlException { Number: UniqueIndexViolation or UniqueConstraintViolation };
}
