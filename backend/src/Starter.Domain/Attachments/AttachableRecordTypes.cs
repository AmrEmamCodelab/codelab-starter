namespace Starter.Domain.Attachments;

/// <summary>
/// The record types that can carry attachments (kebab-case, the <c>{recordType}</c> route segment of
/// <c>/api/records/{recordType}/{recordId}/attachments</c>). Each one is backed by exactly one
/// <c>Starter.Application.Attachments.IAttachableRecordAccess</c>, registered by the module that owns the record.
/// </summary>
public static class AttachableRecordTypes
{
    /// <summary>The Development-only demo record (one fixed record, access by permission only).</summary>
    public const string Demo = "attachments-demo";

    /// <summary>
    /// The PublicId domain label of a record id in the attachment routes (part of the wire format; never rename). A
    /// module that offers its records for attachments hands the client its record id encoded under this label (its own
    /// <c>IIdDomain</c> returning this value), so one route serves every record type.
    /// </summary>
    public const string RecordIdDomainLabel = "attachable-records";
}
