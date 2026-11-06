using Codelab.Security.PublicIds;
using Starter.Domain.Attachments;

namespace Starter.Modules.Records.Api;

/// <summary>
/// The family of record ids on the wire (<c>PublicId&lt;RecordsIds&gt;</c>), one for every record type: the generic
/// record endpoints, the queues and the record-type modules all hand out the same token for the same record.
/// </summary>
public sealed class RecordsIds : IIdDomain
{
    /// <summary>The domain label bound into every record PublicId (part of the wire format; never rename).</summary>
    public const string DomainLabel = "records";

    /// <inheritdoc />
    public static string Domain => DomainLabel;
}

/// <summary>
/// The same record id in the attachment routes' family (<c>attachable-records</c>,
/// <see cref="AttachableRecordTypes.RecordIdDomainLabel"/>), so the detail screen can open the record's files.
/// </summary>
public sealed class RecordAttachmentIds : IIdDomain
{
    /// <inheritdoc />
    public static string Domain => AttachableRecordTypes.RecordIdDomainLabel;
}

/// <summary>The family of user ids (label <c>users</c>, shared with the Users, Access and queue endpoints).</summary>
public sealed class RecordUserIds : IIdDomain
{
    /// <summary>The domain label (shared with every user id family; never rename).</summary>
    public const string DomainLabel = "users";

    /// <inheritdoc />
    public static string Domain => DomainLabel;
}
