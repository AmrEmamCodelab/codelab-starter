using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Starter.Infrastructure.Persistence;
using Starter.Modules.Portal.Domain;

namespace Starter.Modules.Portal.Infrastructure.Persistence;

/// <summary>The Portal module's tables, in the <c>portal</c> schema: contact messages and the content pages.</summary>
public sealed class PortalDbContext(DbContextOptions<PortalDbContext> options) : ModuleDbContext(options)
{
    /// <summary>The database schema of the module.</summary>
    public const string SchemaName = "portal";

    /// <summary>The contact messages.</summary>
    public DbSet<ContactMessage> ContactMessages => Set<ContactMessage>();

    /// <summary>The content page headers (one per <see cref="PortalPage"/>).</summary>
    public DbSet<ContentPage> ContentPages => Set<ContentPage>();

    /// <summary>The content sections.</summary>
    public DbSet<ContentSection> ContentSections => Set<ContentSection>();

    /// <inheritdoc />
    protected override string Schema => SchemaName;
}

/// <summary>The Portal table names.</summary>
public static class PortalTableNames
{
    /// <summary>The contact messages table.</summary>
    public const string ContactMessages = "ContactMessages";

    /// <summary>The content page headers table.</summary>
    public const string ContentPages = "ContentPages";

    /// <summary>The content sections table.</summary>
    public const string ContentSections = "ContentSections";
}

/// <summary>The <c>portal.ContactMessages</c> table: the reference number is unique.</summary>
public sealed class ContactMessageConfiguration : IEntityTypeConfiguration<ContactMessage>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<ContactMessage> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        builder.ToTable(PortalTableNames.ContactMessages);
        builder.HasKey(message => message.Id);
        builder.Property(message => message.Id).UseIdentityColumn();
        builder.Property(message => message.ReferenceNumber).HasMaxLength(PortalLimits.ReferenceNumberMaxLength).IsRequired();
        builder.Property(message => message.FullName).HasMaxLength(PortalLimits.FullNameMaxLength).IsRequired();
        builder.Property(message => message.Email).HasMaxLength(PortalLimits.EmailMaxLength).IsRequired();
        builder.Property(message => message.Message).HasMaxLength(PortalLimits.MessageMaxLength).IsRequired();
        builder.HasIndex(message => message.ReferenceNumber).IsUnique();
    }
}

/// <summary>The <c>portal.ContentPages</c> table: the page's int value is the key (never generated).</summary>
public sealed class ContentPageConfiguration : IEntityTypeConfiguration<ContentPage>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<ContentPage> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        builder.ToTable(PortalTableNames.ContentPages);
        builder.HasKey(page => page.Page);
        builder.Property(page => page.Page).ValueGeneratedNever();
    }
}

/// <summary>The <c>portal.ContentSections</c> table: each row belongs to a page (Restrict), read by page and order.</summary>
public sealed class ContentSectionConfiguration : IEntityTypeConfiguration<ContentSection>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<ContentSection> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        builder.ToTable(PortalTableNames.ContentSections);
        builder.HasKey(section => section.Id);
        builder.Property(section => section.Id).UseIdentityColumn();
        builder.Property(section => section.TitleAr).HasMaxLength(PortalLimits.SectionTitleMaxLength).IsRequired();
        builder.Property(section => section.TitleEn).HasMaxLength(PortalLimits.SectionTitleMaxLength).IsRequired();
        builder.Property(section => section.BodyAr).HasMaxLength(PortalLimits.SectionBodyMaxLength).IsRequired();
        builder.Property(section => section.BodyEn).HasMaxLength(PortalLimits.SectionBodyMaxLength).IsRequired();
        builder.Ignore(section => section.Text);
        builder.HasIndex(section => new { section.Page, section.SortOrder });
        builder.HasOne<ContentPage>().WithMany().HasForeignKey(section => section.Page).OnDelete(DeleteBehavior.Restrict);
    }
}
