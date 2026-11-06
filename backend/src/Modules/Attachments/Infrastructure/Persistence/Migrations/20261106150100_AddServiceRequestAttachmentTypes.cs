using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Starter.Domain.Attachments;
using Starter.Modules.Attachments.Domain;

namespace Starter.Modules.Attachments.Infrastructure.Persistence.Migrations;

/// <summary>
/// Seeds the two attachment types of the sample record type <see cref="AttachableRecordTypes.ServiceRequest"/>
/// (module ServiceRequests): the required request document and the optional supporting files. It adds rows only, so
/// its target model is <see cref="AttachmentsMigrationModel"/>. Frozen once shipped: a change is a new migration with
/// <c>UpdateData</c>. Removing the sample module leaves these rows (retire them with <c>IsActive</c> = false in a new
/// migration if a fork wants them gone from the catalog).
/// </summary>
[DbContext(typeof(AttachmentsDbContext))]
[Migration(MigrationId)]
public sealed class AddServiceRequestAttachmentTypes : Migration
{
    /// <summary>The migration id (UTC timestamp and class name).</summary>
    public const string MigrationId = "20261106150100_AddServiceRequestAttachmentTypes";

    /// <summary>The most request documents one service request may carry.</summary>
    public const int DocumentMaxFiles = 3;

    /// <summary>The most supporting files one service request may carry.</summary>
    public const int SupportingMaxFiles = 5;

    private static readonly string[] TypeColumns =
    [
        nameof(ServiceAttachment.Id), nameof(ServiceAttachment.Key), nameof(ServiceAttachment.NameAr), nameof(ServiceAttachment.NameEn),
        nameof(ServiceAttachment.RecordType), nameof(ServiceAttachment.IsRequired), nameof(ServiceAttachment.MaxFiles),
        nameof(ServiceAttachment.IsActive), AttachmentsTableNames.AllowedExtensionsColumn,
    ];

    private static readonly string[] AllowedExtensions = [Extensions.Pdf, Extensions.Jpg, Extensions.Png];

    /// <summary>The required request document (one to <see cref="DocumentMaxFiles"/> files).</summary>
    public static ServiceAttachmentSeed Document { get; } = new(
        Id: AttachmentTypeIds.ServiceRequestDocument,
        Key: AttachmentTypeKeys.ServiceRequestDocument,
        Names: new AttachmentTypeNames(Ar: "مستند الطلب", En: "Request document"),
        RecordType: AttachableRecordTypes.ServiceRequest,
        Rules: new AttachmentTypeRules(IsRequired: true, MaxFiles: DocumentMaxFiles, AllowedExtensions: AllowedExtensions));

    /// <summary>The optional supporting files (up to <see cref="SupportingMaxFiles"/>).</summary>
    public static ServiceAttachmentSeed Supporting { get; } = new(
        Id: AttachmentTypeIds.ServiceRequestSupporting,
        Key: AttachmentTypeKeys.ServiceRequestSupporting,
        Names: new AttachmentTypeNames(Ar: "مستندات داعمة", En: "Supporting documents"),
        RecordType: AttachableRecordTypes.ServiceRequest,
        Rules: new AttachmentTypeRules(IsRequired: false, MaxFiles: SupportingMaxFiles, AllowedExtensions: AllowedExtensions));

    /// <summary>The rows this migration inserts, in id order.</summary>
    public static IReadOnlyList<ServiceAttachment> Rows { get; } = [ServiceAttachment.Seeded(Document), ServiceAttachment.Seeded(Supporting)];

    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        ArgumentNullException.ThrowIfNull(migrationBuilder);
        foreach (var type in Rows)
        {
            migrationBuilder.InsertData(
                table: AttachmentsTableNames.ServiceAttachments,
                schema: AttachmentsDbContext.SchemaName,
                columns: TypeColumns,
                values: [type.Id, type.Key, type.NameAr, type.NameEn, type.RecordType, type.IsRequired, type.MaxFiles, type.IsActive, type.AllowedExtensionsValue]);
        }
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        ArgumentNullException.ThrowIfNull(migrationBuilder);
        foreach (var type in Rows)
        {
            migrationBuilder.DeleteData(
                table: AttachmentsTableNames.ServiceAttachments,
                schema: AttachmentsDbContext.SchemaName,
                keyColumn: nameof(ServiceAttachment.Id),
                keyValue: type.Id);
        }
    }

    /// <inheritdoc />
    protected override void BuildTargetModel(ModelBuilder modelBuilder) => AttachmentsMigrationModel.Build(modelBuilder);

    /// <summary>The file extensions both types accept (stored form; inside the upload allowlist).</summary>
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
