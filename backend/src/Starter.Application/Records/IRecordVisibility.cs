using Starter.Domain.Records;

namespace Starter.Application.Records;

/// <summary>
/// Whether the current user may see one record, decided by the Records module with the rule its own endpoints apply
/// (the owner with the type's view-own key; anyone else only once the record was submitted, with the queue key or
/// the oversight key, scope reaching the record's organization). A record-type module asks it before it serves its
/// own fields, so its data is never more visible than the record (one visibility policy, owned by Records).
/// </summary>
public interface IRecordVisibility
{
    /// <summary>True when the current user may see <paramref name="record"/>; false for a missing or hidden record and for a caller with no active account.</summary>
    Task<bool> CanViewAsync(RecordRef record, CancellationToken cancellationToken);
}
