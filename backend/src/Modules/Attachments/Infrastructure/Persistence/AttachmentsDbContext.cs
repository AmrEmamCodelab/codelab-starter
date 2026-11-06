using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Starter.Infrastructure.Persistence;
using Starter.Modules.Attachments.Domain;

namespace Starter.Modules.Attachments.Infrastructure.Persistence;

/// <summary>
/// The Attachments module's tables, in the <c>attachments</c> schema only: the attachment type catalog
/// (<see cref="ServiceAttachment"/>, seeded by migrations) and the typed links of files to records
/// (<see cref="RecordAttachment"/>). No file bytes: they live on the attachment service.
/// </summary>
public sealed class AttachmentsDbContext(DbContextOptions<AttachmentsDbContext> options) : ModuleDbContext(options)
{
    /// <summary>The database schema of the module.</summary>
    public const string SchemaName = "attachments";

    /// <summary>The attachment type catalog.</summary>
    public DbSet<ServiceAttachment> Types => Set<ServiceAttachment>();

    /// <summary>The record attachments (live and removed).</summary>
    public DbSet<RecordAttachment> Attachments => Set<RecordAttachment>();

    /// <inheritdoc />
    protected override string Schema => SchemaName;
}

/// <summary>The Attachments table and column names that differ from a property name.</summary>
public static class AttachmentsTableNames
{
    /// <summary>The attachment type catalog table.</summary>
    public const string ServiceAttachments = "ServiceAttachments";

    /// <summary>The record attachments table.</summary>
    public const string RecordAttachments = "RecordAttachments";

    /// <summary>The column that stores <see cref="ServiceAttachment.AllowedExtensions"/> in its stored form (<c>.pdf;.png</c>).</summary>
    public const string AllowedExtensionsColumn = "AllowedExtensions";

    /// <summary>The internal property that the <see cref="AllowedExtensionsColumn"/> column maps.</summary>
    internal const string AllowedExtensionsProperty = nameof(ServiceAttachment.AllowedExtensionsValue);
}

/// <summary>The <c>attachments.ServiceAttachments</c> table: fixed ids from <c>AttachmentTypeIds</c>, a unique key.</summary>
public sealed class ServiceAttachmentConfiguration : IEntityTypeConfiguration<ServiceAttachment>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<ServiceAttachment> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        builder.ToTable(AttachmentsTableNames.ServiceAttachments);
        builder.HasKey(type => type.Id);
        builder.Property(type => type.Id).ValueGeneratedNever();
        builder.Property(type => type.Key).HasMaxLength(AttachmentsLimits.TypeKeyMaxLength).IsRequired();
        builder.Property(type => type.NameAr).HasMaxLength(AttachmentsLimits.NameMaxLength).IsRequired();
        builder.Property(type => type.NameEn).HasMaxLength(AttachmentsLimits.NameMaxLength).IsRequired();
        builder.Property(type => type.RecordType).HasMaxLength(AttachmentsLimits.RecordTypeMaxLength).IsRequired();
        builder.Property(type => type.AllowedExtensionsValue)
            .HasColumnName(AttachmentsTableNames.AllowedExtensionsColumn)
            .HasMaxLength(AttachmentsLimits.AllowedExtensionsMaxLength)
            .IsRequired();
        builder.Ignore(type => type.AllowedExtensions);
        builder.Ignore(type => type.IsEnforcedAsRequired);
        builder.HasIndex(type => type.Key).IsUnique();
        builder.HasIndex(type => type.RecordType);
    }
}

/// <summary>
/// The <c>attachments.RecordAttachments</c> table: a Restrict foreign key to the type (a type with links can never be
/// deleted, only retired), a unique file id (a file is linked at most once) and the per-record read index.
/// </summary>
public sealed class RecordAttachmentConfiguration : IEntityTypeConfiguration<RecordAttachment>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<RecordAttachment> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        builder.ToTable(AttachmentsTableNames.RecordAttachments);
        builder.HasKey(attachment => attachment.Id);
        builder.Property(attachment => attachment.Id).UseIdentityColumn();
        builder.Property(attachment => attachment.RecordType).HasMaxLength(AttachmentsLimits.RecordTypeMaxLength).IsRequired();
        builder.Property(attachment => attachment.FileId).HasMaxLength(AttachmentsLimits.FileIdMaxLength).IsRequired();
        builder.Property(attachment => attachment.FileName).HasMaxLength(AttachmentsLimits.FileNameMaxLength).IsRequired();
        builder.Property(attachment => attachment.Sha256Hex).HasMaxLength(AttachmentsLimits.Sha256HexLength).IsFixedLength().IsUnicode(false).IsRequired();
        builder.Property(attachment => attachment.ContentType).HasMaxLength(AttachmentsLimits.ContentTypeMaxLength).IsRequired();
        builder.Ignore(attachment => attachment.IsLive);
        builder.HasOne<ServiceAttachment>()
            .WithMany()
            .HasForeignKey(attachment => attachment.ServiceAttachmentId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(attachment => attachment.FileId).IsUnique();
        builder.HasIndex(attachment => new { attachment.RecordType, attachment.RecordId });
    }
}
