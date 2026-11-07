using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Starter.Modules.Notifications.Domain;

namespace Starter.Modules.Notifications.Infrastructure.Persistence.Migrations;

/// <summary>The migration identifiers and constraint names of the Notifications schema.</summary>
public static class NotificationsMigrationIds
{
    /// <summary>The id of the only migration since the v1.0 squash (the tables and every seeded template); never changed.</summary>
    public const string InitialCreate = "20261106120000_InitialCreate";

    /// <summary>The primary key of <c>notifications.Notifications</c>.</summary>
    public const string NotificationsPrimaryKey = "PK_Notifications";

    /// <summary>The feed index (user, then id descending reads).</summary>
    public const string NotificationsUserIndex = "IX_Notifications_UserId_Id";

    /// <summary>The primary key of <c>notifications.NotificationTemplates</c>.</summary>
    public const string TemplatesPrimaryKey = "PK_NotificationTemplates";
}

/// <summary>The SQL Server store types and annotations the Notifications migrations write.</summary>
public static class NotificationsSqlTypes
{
    /// <summary>32-bit integer.</summary>
    public const string Int32Column = "int";

    /// <summary>64-bit integer.</summary>
    public const string BigInt = "bigint";

    /// <summary>Date, time and offset.</summary>
    public const string DateTimeOffset = "datetimeoffset";

    /// <summary>Unbounded Unicode text.</summary>
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
/// The Notifications model as of the latest migration: the target model of <see cref="InitialCreate"/> and the model
/// snapshot that <c>dotnet ef migrations add</c> diffs against. A later migration freezes its own copy before this one
/// changes.
/// </summary>
public static class NotificationsMigrationModel
{
    /// <summary>Builds the model.</summary>
    public static void Build(ModelBuilder modelBuilder)
    {
        ArgumentNullException.ThrowIfNull(modelBuilder);
        modelBuilder
            .HasDefaultSchema(NotificationsDbContext.SchemaName)
            .HasAnnotation(NotificationsSqlTypes.ProductVersionAnnotation, NotificationsSqlTypes.ProductVersion)
            .HasAnnotation(RelationalAnnotationNames.MaxIdentifierLength, NotificationsSqlTypes.MaxIdentifierLength);
        modelBuilder.UseIdentityColumns();
        modelBuilder.Entity(typeof(Notification).FullName!, BuildNotification);
        modelBuilder.Entity(typeof(NotificationTemplate).FullName!, BuildTemplate);
    }

    private static void BuildNotification(EntityTypeBuilder entity)
    {
        entity.Property<long>(nameof(Notification.Id)).ValueGeneratedOnAdd().HasColumnType(NotificationsSqlTypes.BigInt).UseIdentityColumn();
        OptionalText(entity, nameof(Notification.BodyAr), NotificationsLimits.BodyTextMaxLength);
        OptionalText(entity, nameof(Notification.BodyEn), NotificationsLimits.BodyTextMaxLength);
        entity.Property<int>(nameof(Notification.Channels)).HasColumnType(NotificationsSqlTypes.Int32Column);
        entity.Property<DateTimeOffset>(nameof(Notification.CreatedAt)).HasColumnType(NotificationsSqlTypes.DateTimeOffset);
        OptionalText(entity, nameof(Notification.EmailAddress), NotificationsLimits.EmailMaxLength);
        entity.Property<DateTimeOffset?>(nameof(Notification.EmailSentAt)).HasColumnType(NotificationsSqlTypes.DateTimeOffset);
        entity.Property<int>(nameof(Notification.EmailState)).HasColumnType(NotificationsSqlTypes.Int32Column);
        entity.Property<int?>(nameof(Notification.Language)).HasColumnType(NotificationsSqlTypes.Int32Column);
        OptionalText(entity, nameof(Notification.Link), NotificationsLimits.LinkMaxLength);
        entity.Property<string>(nameof(Notification.PlaceholdersJson)).HasColumnType(NotificationsSqlTypes.NVarCharMax);
        entity.Property<DateTimeOffset?>(nameof(Notification.ReadAt)).HasColumnType(NotificationsSqlTypes.DateTimeOffset);
        entity.Property<DateTimeOffset?>(nameof(Notification.SmsSentAt)).HasColumnType(NotificationsSqlTypes.DateTimeOffset);
        entity.Property<int>(nameof(Notification.SmsState)).HasColumnType(NotificationsSqlTypes.Int32Column);
        entity.Property<string>(nameof(Notification.TemplateKey)).IsRequired()
            .HasMaxLength(NotificationsLimits.TemplateKeyMaxLength).HasColumnType(NotificationsSqlTypes.NVarChar(NotificationsLimits.TemplateKeyMaxLength));
        OptionalText(entity, nameof(Notification.TitleAr), NotificationsLimits.TitleTextMaxLength);
        OptionalText(entity, nameof(Notification.TitleEn), NotificationsLimits.TitleTextMaxLength);
        entity.Property<long?>(nameof(Notification.UserId)).HasColumnType(NotificationsSqlTypes.BigInt);
        entity.HasKey(nameof(Notification.Id));
        entity.HasIndex(nameof(Notification.UserId), nameof(Notification.Id));
        entity.ToTable(NotificationsTableNames.Notifications, NotificationsDbContext.SchemaName);
    }

    private static void BuildTemplate(EntityTypeBuilder entity)
    {
        entity.Property<string>(nameof(NotificationTemplate.Key))
            .HasMaxLength(NotificationsLimits.TemplateKeyMaxLength).HasColumnType(NotificationsSqlTypes.NVarChar(NotificationsLimits.TemplateKeyMaxLength));
        entity.Property<int>(nameof(NotificationTemplate.Channel)).HasColumnType(NotificationsSqlTypes.Int32Column);
        entity.Property<int>(nameof(NotificationTemplate.Language)).HasColumnType(NotificationsSqlTypes.Int32Column);
        entity.Property<string>(nameof(NotificationTemplate.Body)).IsRequired().HasColumnType(NotificationsSqlTypes.NVarCharMax);
        OptionalText(entity, nameof(NotificationTemplate.Subject), NotificationsLimits.SubjectMaxLength);
        entity.HasKey(nameof(NotificationTemplate.Key), nameof(NotificationTemplate.Channel), nameof(NotificationTemplate.Language));
        entity.ToTable(NotificationsTableNames.Templates, NotificationsDbContext.SchemaName);
    }

    private static void OptionalText(EntityTypeBuilder entity, string name, int maxLength) =>
        entity.Property<string>(name).HasMaxLength(maxLength).HasColumnType(NotificationsSqlTypes.NVarChar(maxLength));
}

/// <summary>The model snapshot <c>dotnet ef migrations add</c> diffs the next migration against.</summary>
[DbContext(typeof(NotificationsDbContext))]
public sealed class NotificationsDbContextModelSnapshot : ModelSnapshot
{
    /// <inheritdoc />
    protected override void BuildModel(ModelBuilder modelBuilder) => NotificationsMigrationModel.Build(modelBuilder);
}
