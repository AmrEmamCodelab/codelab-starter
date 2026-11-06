using Starter.Domain.Records;

namespace Starter.Modules.Records.Domain;

/// <summary>
/// The base row of every record of every record type (<c>records.Records</c>): who owns it, which status it is in and
/// its reference number once submitted. A record type keeps its own fields in its own subtype table keyed by
/// <see cref="Id"/> in its own schema (an owned subtype table); this row never carries record-type data. The status is
/// the record type's int value (a constant of its statuses class); the workflow catalog gives it meaning.
/// </summary>
public sealed class Record
{
    private Record()
    {
    }

    /// <summary>The internal id (identity; the wire carries its PublicId).</summary>
    public long Id { get; private set; }

    /// <summary>The record type key (kebab case), which selects the registration and the workflow catalog.</summary>
    public string RecordType { get; private set; } = string.Empty;

    /// <summary>The reference number <c>PREFIX-YYYY-NNN</c>, or null until the first submit.</summary>
    public string? Number { get; private set; }

    /// <summary>The current status (the record type's int value).</summary>
    public int Status { get; private set; }

    /// <summary>The requester who created the record (internal user id).</summary>
    public long OwnerUserId { get; private set; }

    /// <summary>The owner's organization when the record was created (internal id), or null.</summary>
    public long? OrganizationId { get; private set; }

    /// <summary>When the draft was created.</summary>
    public DateTimeOffset CreatedAt { get; private set; }

    /// <summary>When the record was first submitted, or null for a draft never submitted.</summary>
    public DateTimeOffset? SubmittedAt { get; private set; }

    /// <summary>When the record last changed status.</summary>
    public DateTimeOffset UpdatedAt { get; private set; }

    /// <summary>The SQL Server rowversion: a concurrent change makes the second save fail instead of overwriting.</summary>
    public byte[] RowVersion { get; private set; } = [];

    /// <summary>A new draft in <paramref name="draft"/>'s initial status.</summary>
    /// <exception cref="ArgumentException">The record type is not a valid key or the owner id is below 1.</exception>
    public static Record StartDraft(RecordDraft draft)
    {
        ArgumentNullException.ThrowIfNull(draft);
        ArgumentOutOfRangeException.ThrowIfLessThan(draft.OwnerUserId, RecordRef.MinId, nameof(draft));
        return new Record
        {
            RecordType = RecordTypeKeyFormat.EnsureValid(draft.RecordType, nameof(draft)),
            Status = draft.InitialStatus,
            OwnerUserId = draft.OwnerUserId,
            OrganizationId = draft.OrganizationId,
            CreatedAt = draft.At,
            UpdatedAt = draft.At,
        };
    }

    /// <summary>True when <paramref name="userId"/> owns the record.</summary>
    public bool IsOwnedBy(long userId) => OwnerUserId == userId;

    /// <summary>Moves the record to <paramref name="status"/> at <paramref name="at"/> (the workflow already allowed it).</summary>
    public void MoveTo(int status, DateTimeOffset at)
    {
        Status = status;
        UpdatedAt = at;
    }

    /// <summary>
    /// Records a submit: the number is kept from the first submit (a resubmit never takes a new one) and so is the first
    /// submit time.
    /// </summary>
    /// <exception cref="ArgumentException">The number is blank or longer than <see cref="RecordColumnLimits.NumberMaxLength"/>.</exception>
    /// <exception cref="InvalidOperationException">The record already has a different number.</exception>
    public void MarkSubmitted(string number, DateTimeOffset at)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(number);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(number.Length, RecordColumnLimits.NumberMaxLength, nameof(number));
        if (Number is not null && !string.Equals(Number, number, StringComparison.Ordinal))
        {
            throw new InvalidOperationException($"Record {Id} already has a number; a resubmit keeps it.");
        }

        Number = number;
        SubmittedAt ??= at;
    }

    /// <summary>The kernel identity of this record.</summary>
    public RecordRef ToRef() => new(Id, RecordType);
}

/// <summary>The facts of a new draft (a parameter object, so the owner and the organization cannot be swapped).</summary>
/// <param name="RecordType">The record type key.</param>
/// <param name="InitialStatus">The catalog's initial status.</param>
/// <param name="OwnerUserId">The requester's internal user id.</param>
/// <param name="OrganizationId">The requester's organization, or null.</param>
/// <param name="At">When the draft is created.</param>
public sealed record RecordDraft(string RecordType, int InitialStatus, long OwnerUserId, long? OrganizationId, DateTimeOffset At);

/// <summary>The column sizes of the Records tables (the configurations and the migration use the same constants).</summary>
public static class RecordColumnLimits
{
    /// <summary>The longest record type key.</summary>
    public const int RecordTypeMaxLength = RecordTypeKeyFormat.MaxLength;

    /// <summary>The longest reference number (<c>PREFIX-YYYY-NNN</c>: a 6-letter prefix and a counter that grows past 999).</summary>
    public const int NumberMaxLength = 32;
}
