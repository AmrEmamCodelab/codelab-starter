using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Starter.Infrastructure.Persistence;
using Starter.Modules.Notifications.Domain;

namespace Starter.Modules.Notifications.Infrastructure.Persistence;

/// <summary>The Notifications module's tables, in the <c>notifications</c> schema: the notifications and the template texts.</summary>
public sealed class NotificationsDbContext(DbContextOptions<NotificationsDbContext> options) : ModuleDbContext(options)
{
    /// <summary>The database schema of the module.</summary>
    public const string SchemaName = "notifications";

    /// <summary>The notifications (bell items and deliveries).</summary>
    public DbSet<Notification> Notifications => Set<Notification>();

    /// <summary>The template texts (seeded by migrations).</summary>
    public DbSet<NotificationTemplate> Templates => Set<NotificationTemplate>();

    /// <inheritdoc />
    protected override string Schema => SchemaName;
}

/// <summary>The Notifications table names.</summary>
public static class NotificationsTableNames
{
    /// <summary>The notifications table.</summary>
    public const string Notifications = "Notifications";

    /// <summary>The template texts table.</summary>
    public const string Templates = "NotificationTemplates";
}

/// <summary>The <c>notifications.Notifications</c> table.</summary>
public sealed class NotificationConfiguration : IEntityTypeConfiguration<Notification>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<Notification> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        builder.ToTable(NotificationsTableNames.Notifications);
        builder.HasKey(notification => notification.Id);
        builder.Property(notification => notification.Id).UseIdentityColumn();
        builder.Property(notification => notification.EmailAddress).HasMaxLength(NotificationsLimits.EmailMaxLength);
        builder.Property(notification => notification.TemplateKey).HasMaxLength(NotificationsLimits.TemplateKeyMaxLength).IsRequired();
        builder.Property(notification => notification.Link).HasMaxLength(NotificationsLimits.LinkMaxLength);
        builder.Property(notification => notification.TitleAr).HasMaxLength(NotificationsLimits.TitleTextMaxLength);
        builder.Property(notification => notification.TitleEn).HasMaxLength(NotificationsLimits.TitleTextMaxLength);
        builder.Property(notification => notification.BodyAr).HasMaxLength(NotificationsLimits.BodyTextMaxLength);
        builder.Property(notification => notification.BodyEn).HasMaxLength(NotificationsLimits.BodyTextMaxLength);
        builder.Ignore(notification => notification.IsUnread);
        builder.Ignore(notification => notification.IsInApp);
        builder.HasIndex(notification => new { notification.UserId, notification.Id });
    }
}

/// <summary>The <c>notifications.NotificationTemplates</c> table: one row per key, channel and language.</summary>
public sealed class NotificationTemplateConfiguration : IEntityTypeConfiguration<NotificationTemplate>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<NotificationTemplate> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        builder.ToTable(NotificationsTableNames.Templates);
        builder.HasKey(template => new { template.Key, template.Channel, template.Language });
        builder.Property(template => template.Key).HasMaxLength(NotificationsLimits.TemplateKeyMaxLength);
        builder.Property(template => template.Subject).HasMaxLength(NotificationsLimits.SubjectMaxLength);
        builder.Property(template => template.Body).IsRequired();
    }
}
