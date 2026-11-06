namespace Starter.Application.Attachments;

/// <summary>
/// Whether the current user may see or change the attachments of one record of one record type. The module that owns
/// the record type implements it (one per <see cref="RecordType"/>) and registers it; the Attachments module asks it on
/// every list, ticket, link, download and remove, so the attachments of a record are never more visible than the
/// record itself. Live: implementations read the current user and grants on every call.
/// </summary>
public interface IAttachableRecordAccess
{
    /// <summary>The record type this access decides for (a <c>Starter.Domain.Attachments.AttachableRecordTypes</c> constant).</summary>
    string RecordType { get; }

    /// <summary>True when the current user may list and download the attachments of record <paramref name="recordId"/>; false also for a record that does not exist.</summary>
    Task<bool> CanViewAsync(long recordId, CancellationToken cancellationToken);

    /// <summary>
    /// True when the current user may add and remove attachments of record <paramref name="recordId"/> now (for
    /// example only while the record is a draft); false also for a record that does not exist.
    /// </summary>
    Task<bool> CanChangeAsync(long recordId, CancellationToken cancellationToken);
}
