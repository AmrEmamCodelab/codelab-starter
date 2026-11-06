using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Starter.Modules.Portal.Domain;

namespace Starter.Modules.Portal.Infrastructure.Persistence.Migrations;

/// <summary>The migration identifiers and constraint names of the Portal schema.</summary>
public static class PortalMigrationIds
{
    /// <summary>The id of the first migration.</summary>
    public const string InitialCreate = "20261106150000_InitialCreate";

    /// <summary>The primary key of <c>portal.ContactMessages</c>.</summary>
    public const string ContactMessagesPrimaryKey = "PK_ContactMessages";

    /// <summary>The unique index of the reference number.</summary>
    public const string ReferenceNumberIndex = "IX_ContactMessages_ReferenceNumber";

    /// <summary>The primary key of <c>portal.ContentPages</c>.</summary>
    public const string ContentPagesPrimaryKey = "PK_ContentPages";

    /// <summary>The primary key of <c>portal.ContentSections</c>.</summary>
    public const string ContentSectionsPrimaryKey = "PK_ContentSections";

    /// <summary>The page read index (page, then sort order).</summary>
    public const string SectionsPageIndex = "IX_ContentSections_Page_SortOrder";

    /// <summary>The foreign key from a section to its page (Restrict).</summary>
    public const string SectionsPageForeignKey = "FK_ContentSections_ContentPages_Page";
}

/// <summary>The SQL Server store types and annotations the Portal migrations write.</summary>
public static class PortalSqlTypes
{
    /// <summary>32-bit integer.</summary>
    public const string Int32Column = "int";

    /// <summary>64-bit integer.</summary>
    public const string BigInt = "bigint";

    /// <summary>Date, time and offset.</summary>
    public const string DateTimeOffset = "datetimeoffset";

    /// <summary>Unbounded Unicode text (a body longer than 4000 characters).</summary>
    public const string NVarCharMax = "nvarchar(max)";

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
}

/// <summary>
/// The Portal model as of the latest migration: the target model of <see cref="InitialCreate"/> and the model snapshot
/// that <c>dotnet ef migrations add</c> diffs against. A later migration freezes its own copy before this one changes.
/// </summary>
public static class PortalMigrationModel
{
    /// <summary>Builds the model.</summary>
    public static void Build(ModelBuilder modelBuilder)
    {
        ArgumentNullException.ThrowIfNull(modelBuilder);
        modelBuilder
            .HasDefaultSchema(PortalDbContext.SchemaName)
            .HasAnnotation(PortalSqlTypes.ProductVersionAnnotation, PortalSqlTypes.ProductVersion)
            .HasAnnotation(RelationalAnnotationNames.MaxIdentifierLength, PortalSqlTypes.MaxIdentifierLength);
        modelBuilder.UseIdentityColumns();
        modelBuilder.Entity(typeof(ContactMessage).FullName!, BuildContactMessage);
        modelBuilder.Entity(typeof(ContentPage).FullName!, BuildContentPage);
        modelBuilder.Entity(typeof(ContentSection).FullName!, BuildContentSection);
        modelBuilder.Entity(typeof(ContentSection).FullName!, section => section
            .HasOne(typeof(ContentPage).FullName!, navigationName: null)
            .WithMany()
            .HasForeignKey(nameof(ContentSection.Page))
            .OnDelete(DeleteBehavior.Restrict)
            .IsRequired());
    }

    private static void BuildContactMessage(EntityTypeBuilder entity)
    {
        entity.Property<long>(nameof(ContactMessage.Id)).ValueGeneratedOnAdd().HasColumnType(PortalSqlTypes.BigInt).UseIdentityColumn();
        Text(entity, nameof(ContactMessage.Email), PortalLimits.EmailMaxLength);
        Text(entity, nameof(ContactMessage.FullName), PortalLimits.FullNameMaxLength);
        entity.Property<int>(nameof(ContactMessage.Language)).HasColumnType(PortalSqlTypes.Int32Column);
        Text(entity, nameof(ContactMessage.Message), PortalLimits.MessageMaxLength);
        entity.Property<DateTimeOffset>(nameof(ContactMessage.ReceivedAt)).HasColumnType(PortalSqlTypes.DateTimeOffset);
        Text(entity, nameof(ContactMessage.ReferenceNumber), PortalLimits.ReferenceNumberMaxLength);
        entity.Property<int>(nameof(ContactMessage.TopicId)).HasColumnType(PortalSqlTypes.Int32Column);
        entity.HasKey(nameof(ContactMessage.Id));
        entity.HasIndex(nameof(ContactMessage.ReferenceNumber)).IsUnique();
        entity.ToTable(PortalTableNames.ContactMessages, PortalDbContext.SchemaName);
    }

    private static void BuildContentPage(EntityTypeBuilder entity)
    {
        entity.Property<int>(nameof(ContentPage.Page)).HasColumnType(PortalSqlTypes.Int32Column);
        entity.Property<DateTimeOffset?>(nameof(ContentPage.UpdatedAt)).HasColumnType(PortalSqlTypes.DateTimeOffset);
        entity.HasKey(nameof(ContentPage.Page));
        entity.ToTable(PortalTableNames.ContentPages, PortalDbContext.SchemaName);
    }

    private static void BuildContentSection(EntityTypeBuilder entity)
    {
        entity.Property<long>(nameof(ContentSection.Id)).ValueGeneratedOnAdd().HasColumnType(PortalSqlTypes.BigInt).UseIdentityColumn();
        LongText(entity, nameof(ContentSection.BodyAr));
        LongText(entity, nameof(ContentSection.BodyEn));
        entity.Property<int>(nameof(ContentSection.Page)).HasColumnType(PortalSqlTypes.Int32Column);
        entity.Property<int>(nameof(ContentSection.SortOrder)).HasColumnType(PortalSqlTypes.Int32Column);
        Text(entity, nameof(ContentSection.TitleAr), PortalLimits.SectionTitleMaxLength);
        Text(entity, nameof(ContentSection.TitleEn), PortalLimits.SectionTitleMaxLength);
        entity.HasKey(nameof(ContentSection.Id));
        entity.HasIndex(nameof(ContentSection.Page), nameof(ContentSection.SortOrder));
        entity.ToTable(PortalTableNames.ContentSections, PortalDbContext.SchemaName);
    }

    private static void Text(EntityTypeBuilder entity, string name, int maxLength) =>
        entity.Property<string>(name).IsRequired().HasMaxLength(maxLength).HasColumnType(PortalSqlTypes.NVarChar(maxLength));

    private static void LongText(EntityTypeBuilder entity, string name) =>
        entity.Property<string>(name).IsRequired().HasMaxLength(PortalLimits.SectionBodyMaxLength).HasColumnType(PortalSqlTypes.NVarCharMax);
}

/// <summary>The model snapshot <c>dotnet ef migrations add</c> diffs the next migration against.</summary>
[DbContext(typeof(PortalDbContext))]
public sealed class PortalDbContextModelSnapshot : ModelSnapshot
{
    /// <inheritdoc />
    protected override void BuildModel(ModelBuilder modelBuilder) => PortalMigrationModel.Build(modelBuilder);
}
