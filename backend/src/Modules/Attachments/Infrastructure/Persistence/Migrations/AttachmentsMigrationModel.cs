using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Starter.Modules.Attachments.Domain;

namespace Starter.Modules.Attachments.Infrastructure.Persistence.Migrations;

/// <summary>The migration identifiers and constraint names of the Attachments schema.</summary>
public static class AttachmentsMigrationIds
{
    /// <summary>The id of the only migration since the v1.0 squash (the tables and the Development demo type); never changed.</summary>
    public const string InitialCreate = "20261106150000_InitialCreate";

    /// <summary>The primary key of <c>attachments.ServiceAttachments</c>.</summary>
    public const string ServiceAttachmentsPrimaryKey = "PK_ServiceAttachments";

    /// <summary>The unique index on the type key.</summary>
    public const string ServiceAttachmentsKeyIndex = "IX_ServiceAttachments_Key";

    /// <summary>The index on the record type (the type list of one record type).</summary>
    public const string ServiceAttachmentsRecordTypeIndex = "IX_ServiceAttachments_RecordType";

    /// <summary>The primary key of <c>attachments.RecordAttachments</c>.</summary>
    public const string RecordAttachmentsPrimaryKey = "PK_RecordAttachments";

    /// <summary>The Restrict foreign key from an attachment to its type.</summary>
    public const string RecordAttachmentsTypeForeignKey = "FK_RecordAttachments_ServiceAttachments_ServiceAttachmentId";

    /// <summary>The unique index on the attachment-service file id (a file is linked at most once).</summary>
    public const string RecordAttachmentsFileIdIndex = "IX_RecordAttachments_FileId";

    /// <summary>The index of one record's attachments (record type, record id).</summary>
    public const string RecordAttachmentsRecordIndex = "IX_RecordAttachments_RecordType_RecordId";

    /// <summary>The index behind the foreign key.</summary>
    public const string RecordAttachmentsTypeIndex = "IX_RecordAttachments_ServiceAttachmentId";
}

/// <summary>The SQL Server store types and annotations the Attachments migrations write.</summary>
public static class AttachmentsSqlTypes
{
    /// <summary>32-bit integer.</summary>
    public const string Int32Column = "int";

    /// <summary>64-bit integer.</summary>
    public const string BigInt = "bigint";

    /// <summary>Boolean.</summary>
    public const string Bit = "bit";

    /// <summary>A GUID.</summary>
    public const string UniqueIdentifier = "uniqueidentifier";

    /// <summary>Date, time and offset.</summary>
    public const string DateTimeOffset = "datetimeoffset";

    /// <summary>The identity column annotation.</summary>
    public const string IdentityAnnotation = "SqlServer:Identity";

    /// <summary>Identity seed 1, increment 1.</summary>
    public const string IdentitySeedIncrement = "1, 1";

    /// <summary>The model annotation that records the EF Core version.</summary>
    public const string ProductVersionAnnotation = "ProductVersion";

    /// <summary>The EF Core version of the model.</summary>
    public const string ProductVersion = "10.0.14";

    /// <summary>The longest SQL Server identifier.</summary>
    public const int MaxIdentifierLength = 128;

    /// <summary>A Unicode string column of <paramref name="length"/> characters.</summary>
    public static string NVarChar(int length) => $"nvarchar({length})";

    /// <summary>A fixed-length ASCII column of <paramref name="length"/> characters.</summary>
    public static string FixedAscii(int length) => $"char({length})";
}

/// <summary>
/// The Attachments model as of the latest migration: the target model of every Attachments migration and the model
/// snapshot that <c>dotnet ef migrations add</c> diffs against. Seed migrations (a new attachment type) add rows only,
/// so they share it; a later schema migration freezes its own copy before this one changes. It carries no data.
/// </summary>
public static class AttachmentsMigrationModel
{
    /// <summary>Builds the model.</summary>
    public static void Build(ModelBuilder modelBuilder)
    {
        ArgumentNullException.ThrowIfNull(modelBuilder);
        modelBuilder
            .HasDefaultSchema(AttachmentsDbContext.SchemaName)
            .HasAnnotation(AttachmentsSqlTypes.ProductVersionAnnotation, AttachmentsSqlTypes.ProductVersion)
            .HasAnnotation(RelationalAnnotationNames.MaxIdentifierLength, AttachmentsSqlTypes.MaxIdentifierLength);
        modelBuilder.UseIdentityColumns();
        modelBuilder.Entity(typeof(ServiceAttachment).FullName!, BuildServiceAttachment);
        modelBuilder.Entity(typeof(RecordAttachment).FullName!, BuildRecordAttachment);
        modelBuilder.Entity(typeof(RecordAttachment).FullName!, attachment => attachment
            .HasOne(typeof(ServiceAttachment).FullName!, navigationName: null)
            .WithMany()
            .HasForeignKey(nameof(RecordAttachment.ServiceAttachmentId))
            .OnDelete(DeleteBehavior.Restrict)
            .IsRequired());
    }

    private static void BuildServiceAttachment(EntityTypeBuilder entity)
    {
        entity.Property<int>(nameof(ServiceAttachment.Id)).HasColumnType(AttachmentsSqlTypes.Int32Column);
        Text(entity, AttachmentsTableNames.AllowedExtensionsProperty, AttachmentsLimits.AllowedExtensionsMaxLength)
            .HasColumnName(AttachmentsTableNames.AllowedExtensionsColumn);
        entity.Property<bool>(nameof(ServiceAttachment.IsActive)).HasColumnType(AttachmentsSqlTypes.Bit);
        entity.Property<bool>(nameof(ServiceAttachment.IsRequired)).HasColumnType(AttachmentsSqlTypes.Bit);
        Text(entity, nameof(ServiceAttachment.Key), AttachmentsLimits.TypeKeyMaxLength);
        entity.Property<int>(nameof(ServiceAttachment.MaxFiles)).HasColumnType(AttachmentsSqlTypes.Int32Column);
        Text(entity, nameof(ServiceAttachment.NameAr), AttachmentsLimits.NameMaxLength);
        Text(entity, nameof(ServiceAttachment.NameEn), AttachmentsLimits.NameMaxLength);
        Text(entity, nameof(ServiceAttachment.RecordType), AttachmentsLimits.RecordTypeMaxLength);
        entity.HasKey(nameof(ServiceAttachment.Id));
        entity.HasIndex(nameof(ServiceAttachment.Key)).IsUnique();
        entity.HasIndex(nameof(ServiceAttachment.RecordType));
        entity.ToTable(AttachmentsTableNames.ServiceAttachments, AttachmentsDbContext.SchemaName);
    }

    private static void BuildRecordAttachment(EntityTypeBuilder entity)
    {
        entity.Property<long>(nameof(RecordAttachment.Id)).ValueGeneratedOnAdd().HasColumnType(AttachmentsSqlTypes.BigInt).UseIdentityColumn();
        Text(entity, nameof(RecordAttachment.ContentType), AttachmentsLimits.ContentTypeMaxLength);
        Text(entity, nameof(RecordAttachment.FileId), AttachmentsLimits.FileIdMaxLength);
        Text(entity, nameof(RecordAttachment.FileName), AttachmentsLimits.FileNameMaxLength);
        entity.Property<long>(nameof(RecordAttachment.RecordId)).HasColumnType(AttachmentsSqlTypes.BigInt);
        Text(entity, nameof(RecordAttachment.RecordType), AttachmentsLimits.RecordTypeMaxLength);
        entity.Property<DateTimeOffset?>(nameof(RecordAttachment.RemovedAt)).HasColumnType(AttachmentsSqlTypes.DateTimeOffset);
        entity.Property<Guid?>(nameof(RecordAttachment.RemovedBySubject)).HasColumnType(AttachmentsSqlTypes.UniqueIdentifier);
        entity.Property<int>(nameof(RecordAttachment.ServiceAttachmentId)).HasColumnType(AttachmentsSqlTypes.Int32Column);
        entity.Property<string>(nameof(RecordAttachment.Sha256Hex)).IsRequired()
            .HasMaxLength(AttachmentsLimits.Sha256HexLength).IsUnicode(false).IsFixedLength()
            .HasColumnType(AttachmentsSqlTypes.FixedAscii(AttachmentsLimits.Sha256HexLength));
        entity.Property<long>(nameof(RecordAttachment.SizeBytes)).HasColumnType(AttachmentsSqlTypes.BigInt);
        entity.Property<DateTimeOffset>(nameof(RecordAttachment.UploadedAt)).HasColumnType(AttachmentsSqlTypes.DateTimeOffset);
        entity.Property<Guid>(nameof(RecordAttachment.UploadedBySubject)).HasColumnType(AttachmentsSqlTypes.UniqueIdentifier);
        entity.HasKey(nameof(RecordAttachment.Id));
        entity.HasIndex(nameof(RecordAttachment.FileId)).IsUnique();
        entity.HasIndex(nameof(RecordAttachment.ServiceAttachmentId));
        entity.HasIndex(nameof(RecordAttachment.RecordType), nameof(RecordAttachment.RecordId));
        entity.ToTable(AttachmentsTableNames.RecordAttachments, AttachmentsDbContext.SchemaName);
    }

    private static PropertyBuilder<string> Text(EntityTypeBuilder entity, string name, int maxLength) =>
        entity.Property<string>(name).IsRequired().HasMaxLength(maxLength).HasColumnType(AttachmentsSqlTypes.NVarChar(maxLength));
}

/// <summary>The model snapshot <c>dotnet ef migrations add</c> diffs the next migration against.</summary>
[DbContext(typeof(AttachmentsDbContext))]
public sealed class AttachmentsDbContextModelSnapshot : ModelSnapshot
{
    /// <inheritdoc />
    protected override void BuildModel(ModelBuilder modelBuilder) => AttachmentsMigrationModel.Build(modelBuilder);
}
