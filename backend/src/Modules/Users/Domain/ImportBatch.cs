namespace Starter.Modules.Users.Domain;

/// <summary>The state of an import batch. Stored as the integer value.</summary>
public enum ImportBatchStatus
{
    /// <summary>Parsed and validated; waiting for the administrator's commit.</summary>
    Previewed = 1,

    /// <summary>Committed: its valid rows became accounts. A batch commits once.</summary>
    Committed = 2,

    /// <summary>Its lifetime ran out before a commit; it can no longer commit.</summary>
    Expired = 3,
}

/// <summary>Whether an import batch may be committed now by a given administrator.</summary>
public enum ImportCommitDecision
{
    /// <summary>The batch may be committed.</summary>
    Allowed = 1,

    /// <summary>Another administrator previewed it: it is not visible to this one.</summary>
    NotOwner = 2,

    /// <summary>The batch was already committed.</summary>
    AlreadyCommitted = 3,

    /// <summary>The batch lifetime ran out.</summary>
    Expired = 4,
}

/// <summary>
/// One previewed user import: the parsed rows (JSON, the module's own format), the counts, who previewed it and until
/// when it may be committed. Only the administrator who previewed a batch may commit it, once, before it expires.
/// The rows hold names and e-mail addresses (no password, no ticket); see MODULE.md for their lifetime.
/// </summary>
public sealed class ImportBatch
{
    private ImportBatch()
    {
    }

    /// <summary>The internal id (the wire carries its PublicId).</summary>
    public long Id { get; private set; }

    /// <summary>The state (stored as the integer value; also the concurrency token of a commit).</summary>
    public ImportBatchStatus Status { get; private set; }

    /// <summary>The parsed rows as JSON.</summary>
    public string RowsJson { get; private set; } = string.Empty;

    /// <summary>The number of data rows in the sheet.</summary>
    public int RowCount { get; private set; }

    /// <summary>The number of rows that passed validation at preview.</summary>
    public int ValidRowCount { get; private set; }

    /// <summary>The token subject of the administrator who previewed it.</summary>
    public Guid CreatedBySubjectId { get; private set; }

    /// <summary>When it was previewed.</summary>
    public DateTimeOffset CreatedAt { get; private set; }

    /// <summary>The last moment it may be committed.</summary>
    public DateTimeOffset ExpiresAt { get; private set; }

    /// <summary>When it was committed, or null.</summary>
    public DateTimeOffset? CommittedAt { get; private set; }

    /// <summary>A previewed batch that lives for <see cref="ImportBatchDraft.Lifetime"/>.</summary>
    public static ImportBatch Preview(ImportBatchDraft draft, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(draft);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(draft.Lifetime, TimeSpan.Zero);
        return new ImportBatch
        {
            Status = ImportBatchStatus.Previewed,
            RowsJson = draft.RowsJson,
            RowCount = draft.RowCount,
            ValidRowCount = draft.ValidRowCount,
            CreatedBySubjectId = draft.CreatedBySubjectId,
            CreatedAt = now,
            ExpiresAt = now + draft.Lifetime,
        };
    }

    /// <summary>Whether <paramref name="subjectId"/> may commit the batch at <paramref name="now"/>.</summary>
    public ImportCommitDecision CheckCommit(Guid subjectId, DateTimeOffset now)
    {
        if (subjectId != CreatedBySubjectId)
        {
            return ImportCommitDecision.NotOwner;
        }

        if (Status == ImportBatchStatus.Committed)
        {
            return ImportCommitDecision.AlreadyCommitted;
        }

        return Status == ImportBatchStatus.Expired || now > ExpiresAt ? ImportCommitDecision.Expired : ImportCommitDecision.Allowed;
    }

    /// <summary>Marks the batch committed.</summary>
    /// <exception cref="InvalidOperationException">The batch is not in the Previewed state.</exception>
    public void MarkCommitted(DateTimeOffset now)
    {
        RequirePreviewed();
        Status = ImportBatchStatus.Committed;
        CommittedAt = now;
    }

    /// <summary>Marks a batch whose lifetime ran out as expired (a committed batch stays committed).</summary>
    public void MarkExpired()
    {
        if (Status == ImportBatchStatus.Previewed)
        {
            Status = ImportBatchStatus.Expired;
        }
    }

    private void RequirePreviewed()
    {
        if (Status != ImportBatchStatus.Previewed)
        {
            throw new InvalidOperationException($"Import batch {Id} is {(int)Status}, not Previewed.");
        }
    }
}

/// <summary>The values of a new import batch.</summary>
/// <param name="RowsJson">The parsed rows as JSON.</param>
/// <param name="RowCount">The number of data rows.</param>
/// <param name="ValidRowCount">The number of valid rows.</param>
/// <param name="CreatedBySubjectId">The previewing administrator's token subject.</param>
/// <param name="Lifetime">How long the batch may be committed (positive).</param>
public sealed record ImportBatchDraft(string RowsJson, int RowCount, int ValidRowCount, Guid CreatedBySubjectId, TimeSpan Lifetime);
