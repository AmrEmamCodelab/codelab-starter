using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Operations;
using Microsoft.EntityFrameworkCore.Migrations.Operations.Builders;
using Starter.Modules.Attachments.Domain;

namespace Starter.Modules.Attachments.Infrastructure.Persistence.Migrations;

/// <summary>
/// The Attachments schema: the attachment type catalog with the Development demo type (<see cref="AttachmentTypeSeed"/>)
/// and the record attachments, with the Restrict foreign key to the type, the unique file id and the per-record index.
/// Written in the reshaped migration style of the other modules; <see cref="AttachmentsMigrationModel"/> is its target
/// model and the snapshot, and the model test proves the context has no pending change against it. See MODULE.md.
/// </summary>
[DbContext(typeof(AttachmentsDbContext))]
[Migration(AttachmentsMigrationIds.InitialCreate)]
public sealed class InitialCreate : Migration
{
    private static readonly string[] TypeColumns =
    [
        nameof(ServiceAttachment.Id), nameof(ServiceAttachment.Key), nameof(ServiceAttachment.NameAr), nameof(ServiceAttachment.NameEn),
        nameof(ServiceAttachment.RecordType), nameof(ServiceAttachment.IsRequired), nameof(ServiceAttachment.MaxFiles),
        nameof(ServiceAttachment.IsActive), AttachmentsTableNames.AllowedExtensionsColumn,
    ];

    private static readonly string[] RecordIndexColumns = [nameof(RecordAttachment.RecordType), nameof(RecordAttachment.RecordId)];

    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        ArgumentNullException.ThrowIfNull(migrationBuilder);
        migrationBuilder.EnsureSchema(name: AttachmentsDbContext.SchemaName);
        CreateServiceAttachments(migrationBuilder);
        CreateRecordAttachments(migrationBuilder);
        CreateIndexes(migrationBuilder);
        SeedTypes(migrationBuilder);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        ArgumentNullException.ThrowIfNull(migrationBuilder);
        migrationBuilder.DropTable(name: AttachmentsTableNames.RecordAttachments, schema: AttachmentsDbContext.SchemaName);
        migrationBuilder.DropTable(name: AttachmentsTableNames.ServiceAttachments, schema: AttachmentsDbContext.SchemaName);
    }

    /// <inheritdoc />
    protected override void BuildTargetModel(ModelBuilder modelBuilder) => AttachmentsMigrationModel.Build(modelBuilder);

    private static void CreateServiceAttachments(MigrationBuilder migrationBuilder) =>
        migrationBuilder.CreateTable(
            name: AttachmentsTableNames.ServiceAttachments,
            schema: AttachmentsDbContext.SchemaName,
            columns: table => new
            {
                Id = Integer(table),
                Key = Text(table, AttachmentsLimits.TypeKeyMaxLength),
                NameAr = Text(table, AttachmentsLimits.NameMaxLength),
                NameEn = Text(table, AttachmentsLimits.NameMaxLength),
                RecordType = Text(table, AttachmentsLimits.RecordTypeMaxLength),
                IsRequired = Flag(table),
                MaxFiles = Integer(table),
                IsActive = Flag(table),
                AllowedExtensions = Text(table, AttachmentsLimits.AllowedExtensionsMaxLength),
            },
            constraints: table => table.PrimaryKey(AttachmentsMigrationIds.ServiceAttachmentsPrimaryKey, type => type.Id));

    private static void CreateRecordAttachments(MigrationBuilder migrationBuilder) =>
        migrationBuilder.CreateTable(
            name: AttachmentsTableNames.RecordAttachments,
            schema: AttachmentsDbContext.SchemaName,
            columns: table => new
            {
                Id = table.Column<long>(type: AttachmentsSqlTypes.BigInt, nullable: false)
                    .Annotation(AttachmentsSqlTypes.IdentityAnnotation, AttachmentsSqlTypes.IdentitySeedIncrement),
                RecordType = Text(table, AttachmentsLimits.RecordTypeMaxLength),
                RecordId = table.Column<long>(type: AttachmentsSqlTypes.BigInt, nullable: false),
                ServiceAttachmentId = Integer(table),
                FileId = Text(table, AttachmentsLimits.FileIdMaxLength),
                FileName = Text(table, AttachmentsLimits.FileNameMaxLength),
                SizeBytes = table.Column<long>(type: AttachmentsSqlTypes.BigInt, nullable: false),
                Sha256Hex = table.Column<string>(
                    type: AttachmentsSqlTypes.FixedAscii(AttachmentsLimits.Sha256HexLength),
                    unicode: false,
                    fixedLength: true,
                    maxLength: AttachmentsLimits.Sha256HexLength,
                    nullable: false),
                ContentType = Text(table, AttachmentsLimits.ContentTypeMaxLength),
                UploadedBySubject = table.Column<Guid>(type: AttachmentsSqlTypes.UniqueIdentifier, nullable: false),
                UploadedAt = table.Column<DateTimeOffset>(type: AttachmentsSqlTypes.DateTimeOffset, nullable: false),
                RemovedAt = table.Column<DateTimeOffset>(type: AttachmentsSqlTypes.DateTimeOffset, nullable: true),
                RemovedBySubject = table.Column<Guid>(type: AttachmentsSqlTypes.UniqueIdentifier, nullable: true),
            },
            constraints: table =>
            {
                table.PrimaryKey(AttachmentsMigrationIds.RecordAttachmentsPrimaryKey, attachment => attachment.Id);
                table.ForeignKey(
                    name: AttachmentsMigrationIds.RecordAttachmentsTypeForeignKey,
                    column: attachment => attachment.ServiceAttachmentId,
                    principalSchema: AttachmentsDbContext.SchemaName,
                    principalTable: AttachmentsTableNames.ServiceAttachments,
                    principalColumn: nameof(ServiceAttachment.Id),
                    onDelete: ReferentialAction.Restrict);
            });

    private static void CreateIndexes(MigrationBuilder migrationBuilder)
    {
        CreateIndex(migrationBuilder, AttachmentsMigrationIds.ServiceAttachmentsKeyIndex, AttachmentsTableNames.ServiceAttachments, [nameof(ServiceAttachment.Key)], unique: true);
        CreateIndex(migrationBuilder, AttachmentsMigrationIds.ServiceAttachmentsRecordTypeIndex, AttachmentsTableNames.ServiceAttachments, [nameof(ServiceAttachment.RecordType)], unique: false);
        CreateIndex(migrationBuilder, AttachmentsMigrationIds.RecordAttachmentsFileIdIndex, AttachmentsTableNames.RecordAttachments, [nameof(RecordAttachment.FileId)], unique: true);
        CreateIndex(migrationBuilder, AttachmentsMigrationIds.RecordAttachmentsRecordIndex, AttachmentsTableNames.RecordAttachments, RecordIndexColumns, unique: false);
        CreateIndex(migrationBuilder, AttachmentsMigrationIds.RecordAttachmentsTypeIndex, AttachmentsTableNames.RecordAttachments, [nameof(RecordAttachment.ServiceAttachmentId)], unique: false);
    }

    private static void CreateIndex(MigrationBuilder migrationBuilder, string name, string table, string[] columns, bool unique) =>
        migrationBuilder.CreateIndex(name: name, schema: AttachmentsDbContext.SchemaName, table: table, columns: columns, unique: unique);

    private static void SeedTypes(MigrationBuilder migrationBuilder)
    {
        foreach (var type in AttachmentTypeSeed.Rows)
        {
            migrationBuilder.InsertData(
                table: AttachmentsTableNames.ServiceAttachments,
                schema: AttachmentsDbContext.SchemaName,
                columns: TypeColumns,
                values: [type.Id, type.Key, type.NameAr, type.NameEn, type.RecordType, type.IsRequired, type.MaxFiles, type.IsActive, type.AllowedExtensionsValue]);
        }
    }

    private static OperationBuilder<AddColumnOperation> Integer(ColumnsBuilder table) =>
        table.Column<int>(type: AttachmentsSqlTypes.Int32Column, nullable: false);

    private static OperationBuilder<AddColumnOperation> Flag(ColumnsBuilder table) =>
        table.Column<bool>(type: AttachmentsSqlTypes.Bit, nullable: false);

    private static OperationBuilder<AddColumnOperation> Text(ColumnsBuilder table, int maxLength) =>
        table.Column<string>(type: AttachmentsSqlTypes.NVarChar(maxLength), maxLength: maxLength, nullable: false);
}
