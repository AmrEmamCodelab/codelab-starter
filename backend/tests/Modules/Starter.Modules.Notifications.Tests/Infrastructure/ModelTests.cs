using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Starter.Modules.Notifications.Domain;
using Starter.Modules.Notifications.Infrastructure.Persistence;
using Starter.Modules.Notifications.Infrastructure.Persistence.Migrations;

namespace Starter.Modules.Notifications.Tests.Infrastructure;

public sealed class NotificationsModelTests
{
    [Fact]
    public void The_model_lives_in_the_notifications_schema_and_matches_the_InitialCreate_migration()
    {
        using var context = NewContext();

        Assert.Equal(NotificationsDbContext.SchemaName, context.Model.GetDefaultSchema());
        Assert.Equal(NotificationsTableNames.Notifications, context.Model.FindEntityType(typeof(Notification))!.GetTableName());
        Assert.Equal(NotificationsTableNames.Templates, context.Model.FindEntityType(typeof(NotificationTemplate))!.GetTableName());
        Assert.Equal([NotificationsMigrationIds.InitialCreate], context.Database.GetMigrations());
        Assert.False(context.Database.HasPendingModelChanges());
    }

    [Fact]
    public void Enums_are_int_columns()
    {
        using var context = NewContext();
        var notification = context.Model.FindEntityType(typeof(Notification))!;
        var template = context.Model.FindEntityType(typeof(NotificationTemplate))!;

        foreach (var property in new[]
                 {
                     notification.FindProperty(nameof(Notification.Channels))!, notification.FindProperty(nameof(Notification.EmailState))!,
                     notification.FindProperty(nameof(Notification.SmsState))!, notification.FindProperty(nameof(Notification.Language))!,
                     template.FindProperty(nameof(NotificationTemplate.Channel))!, template.FindProperty(nameof(NotificationTemplate.Language))!,
                 })
        {
            Assert.Equal("int", property.GetColumnType());
        }
    }

    [Fact]
    public void The_migration_script_creates_both_tables_the_feed_index_and_seeds_every_template_text()
    {
        using var context = NewContext();

        var script = context.GetService<IMigrator>().GenerateScript();

        Assert.Contains("CREATE TABLE [notifications].[Notifications]", script, StringComparison.Ordinal);
        Assert.Contains("CREATE TABLE [notifications].[NotificationTemplates]", script, StringComparison.Ordinal);
        Assert.Contains($"CREATE INDEX [{NotificationsMigrationIds.NotificationsUserIndex}]", script, StringComparison.Ordinal);
        Assert.Contains("[notifications].[__EFMigrationsHistory]", script, StringComparison.Ordinal);
        Assert.Equal(TemplateSeed.Rows.Count, script.Split("INSERT INTO [notifications].[NotificationTemplates]").Length - 1);
        Assert.Contains("N'إشعار تجريبي'", script, StringComparison.Ordinal);
    }

    private static NotificationsDbContext NewContext() => new(new DbContextOptionsBuilder<NotificationsDbContext>()
        .UseSqlServer("Server=.;Database=NeverOpened", sql => sql.MigrationsHistoryTable(HistoryRepository.DefaultTableName, NotificationsDbContext.SchemaName))
        .Options);
}
