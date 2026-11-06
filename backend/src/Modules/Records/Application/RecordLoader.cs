using Starter.Application.Records;
using Starter.Application.Results;
using Starter.Domain.Records;
using Starter.Modules.Records.Domain;

namespace Starter.Modules.Records.Application;

/// <summary>A record, its type and what the caller may do with it, loaded once for one request.</summary>
/// <param name="Type">The record type.</param>
/// <param name="Record">The record (tracked).</param>
/// <param name="Standing">The caller's standing on it (always visible: the loader hides the rest).</param>
public sealed record LoadedRecord(RegisteredRecordType Type, Record Record, RecordStanding Standing);

/// <summary>
/// The one gate every per-record use case passes: the caller must be an active account (403
/// <see cref="RecordsModuleErrorCodes.ActorUnknown"/>), the record type registered and the record of that type, and the
/// caller allowed to see it. A missing record and a hidden one answer the same 404 <see cref="RecordsErrorCodes.NotFound"/>,
/// so existence never leaks.
/// </summary>
public sealed class RecordLoader(RecordTypes types, IRecordStore store, RecordCallers callers, RecordStandings standings)
{
    /// <summary>Loads record <paramref name="recordId"/> of <paramref name="recordType"/> (both as a route names them).</summary>
    public async Task<Outcome<LoadedRecord>> LoadAsync(string? recordType, long recordId, CancellationToken cancellationToken)
    {
        var caller = await callers.CurrentAsync(cancellationToken);
        if (caller is null)
        {
            return Refusal.Forbidden(RecordsModuleErrorCodes.ActorUnknown);
        }

        var type = types.Find(recordType);
        if (type is null || recordId < RecordRef.MinId)
        {
            return Refusal.NotFound(RecordsErrorCodes.NotFound);
        }

        var record = await store.FindAsync(recordId, cancellationToken);
        if (record is null || !string.Equals(record.RecordType, type.RecordType, StringComparison.Ordinal))
        {
            return Refusal.NotFound(RecordsErrorCodes.NotFound);
        }

        var standing = await standings.ForAsync(caller, record, type, cancellationToken);
        return standing.CanView ? new LoadedRecord(type, record, standing) : Refusal.NotFound(RecordsErrorCodes.NotFound);
    }

    /// <summary>Loads the record <paramref name="reference"/> names.</summary>
    public Task<Outcome<LoadedRecord>> LoadAsync(RecordRef reference, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(reference);
        return LoadAsync(reference.RecordType, reference.Id, cancellationToken);
    }
}
