using System.Text.Json;
using Codelab.Security.PublicIds;
using Starter.Application.Results;
using Starter.Application.Users;
using Starter.Modules.Users.Domain;

namespace Starter.Modules.Users.Application.Import;

/// <summary>
/// The lifecycle of import batches: a preview stores the rows as read (not the verdicts, which the commit recomputes);
/// a commit finds the caller's own unexpired batch, then claims it once (the status is the concurrency token, so two
/// commits of one batch cannot both succeed).
/// </summary>
public sealed class ImportBatches(
    IImportBatchRepository batches,
    IUsersUnitOfWork unitOfWork,
    ICurrentUserContext currentUser,
    ImportSettings settings,
    TimeProvider timeProvider)
{
    private static readonly JsonSerializerOptions RowsJson = new(JsonSerializerDefaults.Web);

    /// <summary>Stores a previewed batch of <paramref name="rows"/> and returns it (with its id).</summary>
    public async Task<ImportBatch> SavePreviewAsync(IReadOnlyList<CheckedImportRow> rows, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(rows);
        var draft = new ImportBatchDraft(
            RowsJson: JsonSerializer.Serialize(rows.Select(row => row.Row).ToArray(), RowsJson),
            RowCount: rows.Count,
            ValidRowCount: rows.Count(row => row.IsValid),
            CreatedBySubjectId: Subject(),
            Lifetime: settings.BatchLifetime);
        var batch = ImportBatch.Preview(draft, timeProvider.GetUtcNow());
        batches.Add(batch);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return batch;
    }

    /// <summary>
    /// The caller's batch while it may still be committed, or a refusal: <see cref="UsersErrorCodes.ImportBatchNotFound"/>
    /// (unknown or another administrator's), <see cref="UsersErrorCodes.ImportBatchCommitted"/>,
    /// <see cref="UsersErrorCodes.ImportBatchExpired"/> (and the batch is marked expired).
    /// </summary>
    public async Task<Outcome<ImportBatch>> FindCommittableAsync(PublicId<UserImportIds> id, CancellationToken cancellationToken)
    {
        if (await batches.FindAsync(id.Value, cancellationToken) is not { } batch)
        {
            return Refusal.NotFound(UsersErrorCodes.ImportBatchNotFound);
        }

        switch (batch.CheckCommit(Subject(), timeProvider.GetUtcNow()))
        {
            case ImportCommitDecision.Allowed:
                return batch;
            case ImportCommitDecision.AlreadyCommitted:
                return Refusal.Conflict(UsersErrorCodes.ImportBatchCommitted);
            case ImportCommitDecision.Expired:
                batch.MarkExpired();
                await unitOfWork.SaveChangesAsync(cancellationToken);
                return Refusal.Conflict(UsersErrorCodes.ImportBatchExpired);
            default:
                return Refusal.NotFound(UsersErrorCodes.ImportBatchNotFound);
        }
    }

    /// <summary>Marks <paramref name="batch"/> committed; false when a concurrent commit claimed it first.</summary>
    public async Task<bool> TryClaimAsync(ImportBatch batch, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(batch);
        batch.MarkCommitted(timeProvider.GetUtcNow());
        return await unitOfWork.TrySaveChangesAsync(cancellationToken);
    }

    /// <summary>The rows stored with <paramref name="batch"/>.</summary>
    public static IReadOnlyList<ImportRow> RowsOf(ImportBatch batch)
    {
        ArgumentNullException.ThrowIfNull(batch);
        return JsonSerializer.Deserialize<ImportRow[]>(batch.RowsJson, RowsJson) ?? [];
    }

    private Guid Subject() =>
        currentUser.SubjectId ?? throw new InvalidOperationException("The import endpoints require a signed-in user with a subject.");
}
