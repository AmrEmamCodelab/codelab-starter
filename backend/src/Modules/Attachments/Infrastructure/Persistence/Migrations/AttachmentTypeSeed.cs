using Codelab.Security.Uploads;
using Starter.Domain.Attachments;
using Starter.Modules.Attachments.Domain;

namespace Starter.Modules.Attachments.Infrastructure.Persistence.Migrations;

/// <summary>
/// The attachment types <see cref="InitialCreate"/> inserts. Frozen with the migration: a changed or new type is a NEW
/// migration (docs/recipes/add-attachment-type.md), never an edit here. Each row goes through
/// <see cref="ServiceAttachment.Seeded"/>, so a malformed seed fails the migration's own tests, not a database.
/// </summary>
public static class AttachmentTypeSeed
{
    /// <summary>The most files one demo record may hold.</summary>
    public const int DemoDocumentMaxFiles = 5;

    /// <summary>
    /// The Development demo type: <see cref="AttachmentTypeKeys.DemoDocument"/> on record type
    /// <see cref="AttachableRecordTypes.Demo"/>, required, PDF and images, at most <see cref="DemoDocumentMaxFiles"/>.
    /// The row exists in every environment; only Development registers the demo record type's access, so elsewhere
    /// no record of that type can be reached.
    /// </summary>
    public static ServiceAttachmentSeed DemoDocument { get; } = new(
        Id: AttachmentTypeIds.DemoDocument,
        Key: AttachmentTypeKeys.DemoDocument,
        Names: new AttachmentTypeNames(Ar: "مستند تجريبي", En: "Demo document"),
        RecordType: AttachableRecordTypes.Demo,
        Rules: new AttachmentTypeRules(
            IsRequired: true,
            MaxFiles: DemoDocumentMaxFiles,
            AllowedExtensions: [DemoExtensions.Pdf, DemoExtensions.Png, DemoExtensions.Jpg, DemoExtensions.Jpeg]));

    /// <summary>The seeded types, validated by <see cref="ServiceAttachment.Seeded"/>.</summary>
    public static IReadOnlyList<ServiceAttachment> Rows { get; } = [ServiceAttachment.Seeded(DemoDocument)];

    /// <summary>The demo type's extensions (keys of <see cref="UploadContentTypes.ByExtension"/>).</summary>
    public static class DemoExtensions
    {
        /// <summary>PDF.</summary>
        public const string Pdf = ".pdf";

        /// <summary>PNG.</summary>
        public const string Png = ".png";

        /// <summary>JPEG, short form.</summary>
        public const string Jpg = ".jpg";

        /// <summary>JPEG, long form.</summary>
        public const string Jpeg = ".jpeg";
    }
}
