using Starter.Application.Attachments;
using Starter.Application.Results;
using Starter.Modules.Attachments.Domain;

namespace Starter.Modules.Attachments.Application;

/// <summary>One record that can carry attachments, as a route names it.</summary>
/// <param name="RecordType">The record type from the route (may be unknown or malformed until checked).</param>
/// <param name="RecordId">The record's internal id (decoded from its PublicId).</param>
public sealed record AttachableRecord(string? RecordType, long RecordId);

/// <summary>What a caller wants to do with a record's attachments. Stored nowhere; values are explicit.</summary>
public enum RecordAccessKind
{
    /// <summary>List and download.</summary>
    View = 1,

    /// <summary>Get a ticket and link or remove files.</summary>
    Change = 2,
}

/// <summary>
/// The registered <see cref="IAttachableRecordAccess"/> implementations, one per record type: the one gate every use
/// case passes before it touches a record's attachments, so list, link, download and remove apply the same rule.
/// </summary>
public sealed class AttachableRecords
{
    private readonly Dictionary<string, IAttachableRecordAccess> _byRecordType;

    /// <summary>Indexes <paramref name="accesses"/> by record type.</summary>
    /// <exception cref="InvalidOperationException">Two implementations claim the same record type (a wiring bug).</exception>
    public AttachableRecords(IEnumerable<IAttachableRecordAccess> accesses)
    {
        ArgumentNullException.ThrowIfNull(accesses);
        _byRecordType = new Dictionary<string, IAttachableRecordAccess>(StringComparer.Ordinal);
        foreach (var access in accesses)
        {
            if (!_byRecordType.TryAdd(access.RecordType, access))
            {
                throw new InvalidOperationException($"Two attachable record accesses are registered for record type '{access.RecordType}'.");
            }
        }
    }

    /// <summary>True when <paramref name="recordType"/> is well formed and has a registered access.</summary>
    public bool IsKnown(string? recordType) => AttachmentsLimits.IsRecordType(recordType) && _byRecordType.ContainsKey(recordType!);

    /// <summary>
    /// Null when the current user may do <paramref name="kind"/> on <paramref name="record"/>'s attachments; otherwise
    /// the refusal: <see cref="AttachmentsErrorCodes.RecordTypeUnknown"/> (404) for an unknown record type, and
    /// <see cref="AttachmentsErrorCodes.NotFound"/> (404) for a missing record or one the user may not use that way.
    /// </summary>
    public async Task<Refusal?> RefuseAsync(AttachableRecord record, RecordAccessKind kind, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(record);
        if (!IsKnown(record.RecordType))
        {
            return Refusal.NotFound(AttachmentsErrorCodes.RecordTypeUnknown);
        }

        if (record.RecordId < 1)
        {
            return Refusal.NotFound(AttachmentsErrorCodes.NotFound);
        }

        var access = _byRecordType[record.RecordType!];
        var allowed = kind is RecordAccessKind.View
            ? await access.CanViewAsync(record.RecordId, cancellationToken)
            : await access.CanChangeAsync(record.RecordId, cancellationToken);
        return allowed ? null : Refusal.NotFound(AttachmentsErrorCodes.NotFound);
    }
}
