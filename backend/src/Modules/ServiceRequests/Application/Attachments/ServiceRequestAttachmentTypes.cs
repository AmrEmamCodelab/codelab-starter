using Starter.Application.Attachments;
using Starter.Domain.Attachments;
using Starter.Modules.ServiceRequests.Domain;

namespace Starter.Modules.ServiceRequests.Application.Attachments;

/// <summary>
/// The attachment types of the sample, declared here, in the module that owns the record type, through the kernel seam
/// <see cref="IAttachmentTypeSeedSource"/>: the Attachments module writes them to its catalog on every start (inserted
/// when missing, refreshed when changed, matched by key), so deleting this module seeds nothing. Ids and keys are the
/// <see cref="AttachmentTypeIds"/> / <see cref="AttachmentTypeKeys"/> constants in Starter.Domain, which stay reserved
/// after the sample is removed (never reuse them).
/// </summary>
public sealed class ServiceRequestAttachmentTypes : IAttachmentTypeSeedSource
{
    /// <summary>The most request documents one service request may carry.</summary>
    public const int DocumentMaxFiles = 3;

    /// <summary>The most supporting files one service request may carry.</summary>
    public const int SupportingMaxFiles = 5;

    /// <summary>The file extensions both types accept (inside the upload allowlist <c>Codelab:Security:Uploads</c>).</summary>
    public static IReadOnlyList<string> AllowedExtensions { get; } = [Extensions.Pdf, Extensions.Jpg, Extensions.Png];

    /// <summary>The required request document (one to <see cref="DocumentMaxFiles"/> files; the submit interlock).</summary>
    public static AttachmentTypeDefinition Document { get; } = new(
        Id: AttachmentTypeIds.ServiceRequestDocument,
        Key: AttachmentTypeKeys.ServiceRequestDocument,
        RecordType: ServiceRequestRecord.AttachableRecordType,
        NameAr: "مستند الطلب",
        NameEn: "Request document",
        IsRequired: true,
        MaxFiles: DocumentMaxFiles,
        AllowedExtensions: AllowedExtensions);

    /// <summary>The optional supporting files (up to <see cref="SupportingMaxFiles"/>).</summary>
    public static AttachmentTypeDefinition Supporting { get; } = new(
        Id: AttachmentTypeIds.ServiceRequestSupporting,
        Key: AttachmentTypeKeys.ServiceRequestSupporting,
        RecordType: ServiceRequestRecord.AttachableRecordType,
        NameAr: "مستندات داعمة",
        NameEn: "Supporting documents",
        IsRequired: false,
        MaxFiles: SupportingMaxFiles,
        AllowedExtensions: AllowedExtensions);

    /// <summary>Both types, in id order.</summary>
    public static IReadOnlyList<AttachmentTypeDefinition> All { get; } = [Document, Supporting];

    /// <inheritdoc />
    public IEnumerable<AttachmentTypeDefinition> Types => All;

    /// <summary>The accepted extensions (stored form: lower case, with the dot).</summary>
    public static class Extensions
    {
        /// <summary>PDF.</summary>
        public const string Pdf = ".pdf";

        /// <summary>JPEG.</summary>
        public const string Jpg = ".jpg";

        /// <summary>PNG.</summary>
        public const string Png = ".png";
    }
}
