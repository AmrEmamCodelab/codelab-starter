using Starter.Modules.Attachments.Application;

namespace Starter.Modules.Attachments.Api;

/// <summary>
/// The wire labels of the Attachments ids, next to the routes. The id families themselves are
/// <see cref="AttachmentIds"/> (attachments) and <see cref="AttachableRecordIds"/> (record ids in the attachment routes),
/// declared once in Application; this class declares no family of its own.
/// </summary>
public static class AttachmentsIds
{
    /// <summary>The label of attachment PublicIds (<see cref="AttachmentIds.DomainLabel"/>).</summary>
    public const string DomainLabel = AttachmentIds.DomainLabel;

    /// <summary>The label of record PublicIds in the attachment routes (<see cref="AttachableRecordIds.DomainLabel"/>).</summary>
    public const string RecordDomainLabel = AttachableRecordIds.DomainLabel;
}
