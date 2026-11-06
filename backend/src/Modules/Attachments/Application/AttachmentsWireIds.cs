using Codelab.Security.PublicIds;
using Starter.Domain.Attachments;

namespace Starter.Modules.Attachments.Application;

/// <summary>The family of record attachment ids on the wire (<c>PublicId&lt;AttachmentIds&gt;</c>).</summary>
public sealed class AttachmentIds : IIdDomain
{
    /// <summary>The domain label bound into every attachment PublicId (part of the wire format; never rename).</summary>
    public const string DomainLabel = "attachments";

    /// <inheritdoc />
    public static string Domain => DomainLabel;
}

/// <summary>
/// The family of record ids in the attachment routes (<c>PublicId&lt;AttachableRecordIds&gt;</c>), one for every
/// record type. The module that owns a record type encodes its record ids for these routes under the same label
/// (<see cref="AttachableRecordTypes.RecordIdDomainLabel"/>) with its own <see cref="IIdDomain"/>.
/// </summary>
public sealed class AttachableRecordIds : IIdDomain
{
    /// <summary>The domain label (part of the wire format; never rename).</summary>
    public const string DomainLabel = AttachableRecordTypes.RecordIdDomainLabel;

    /// <inheritdoc />
    public static string Domain => DomainLabel;
}
