using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Operations;
using Microsoft.EntityFrameworkCore.Migrations.Operations.Builders;
using Starter.Modules.Notifications.Domain;

namespace Starter.Modules.Notifications.Infrastructure.Persistence.Migrations;

/// <summary>
/// The Notifications schema: the notifications table (bell items and per-channel delivery state) and the template
/// texts with the seeded templates (<see cref="TemplateSeed"/>). Written in the reshaped migration style of the other
/// modules; <see cref="NotificationsMigrationModel"/> is its target model and the snapshot, and the model test proves
/// the context has no pending change against it. See MODULE.md.
/// </summary>
[DbContext(typeof(NotificationsDbContext))]
[Migration(NotificationsMigrationIds.InitialCreate)]
public sealed class InitialCreate : Migration
{
    private static readonly string[] TemplateColumns =
    [
        nameof(NotificationTemplate.Key), nameof(NotificationTemplate.Channel), nameof(NotificationTemplate.Language),
        nameof(NotificationTemplate.Subject), nameof(NotificationTemplate.Body),
    ];

    private static readonly string[] FeedIndexColumns = [nameof(Notification.UserId), nameof(Notification.Id)];

    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        ArgumentNullException.ThrowIfNull(migrationBuilder);
        migrationBuilder.EnsureSchema(name: NotificationsDbContext.SchemaName);
        CreateNotifications(migrationBuilder);
        CreateTemplates(migrationBuilder);
        migrationBuilder.CreateIndex(
            name: NotificationsMigrationIds.NotificationsUserIndex,
            schema: NotificationsDbContext.SchemaName,
            table: NotificationsTableNames.Notifications,
            columns: FeedIndexColumns);
        SeedTemplates(migrationBuilder);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        ArgumentNullException.ThrowIfNull(migrationBuilder);
        migrationBuilder.DropTable(name: NotificationsTableNames.Templates, schema: NotificationsDbContext.SchemaName);
        migrationBuilder.DropTable(name: NotificationsTableNames.Notifications, schema: NotificationsDbContext.SchemaName);
    }

    /// <inheritdoc />
    protected override void BuildTargetModel(ModelBuilder modelBuilder) => NotificationsMigrationModel.Build(modelBuilder);

    private static void CreateNotifications(MigrationBuilder migrationBuilder) =>
        migrationBuilder.CreateTable(
            name: NotificationsTableNames.Notifications,
            schema: NotificationsDbContext.SchemaName,
            columns: table => new
            {
                Id = table.Column<long>(type: NotificationsSqlTypes.BigInt, nullable: false)
                    .Annotation(NotificationsSqlTypes.IdentityAnnotation, NotificationsSqlTypes.IdentitySeedIncrement),
                UserId = table.Column<long>(type: NotificationsSqlTypes.BigInt, nullable: true),
                EmailAddress = OptionalText(table, NotificationsLimits.EmailMaxLength),
                Language = table.Column<int>(type: NotificationsSqlTypes.Int32Column, nullable: true),
                TemplateKey = table.Column<string>(
                    type: NotificationsSqlTypes.NVarChar(NotificationsLimits.TemplateKeyMaxLength),
                    maxLength: NotificationsLimits.TemplateKeyMaxLength,
                    nullable: false),
                PlaceholdersJson = table.Column<string>(type: NotificationsSqlTypes.NVarCharMax, nullable: true),
                Link = OptionalText(table, NotificationsLimits.LinkMaxLength),
                Channels = Integer(table),
                TitleAr = OptionalText(table, NotificationsLimits.TitleTextMaxLength),
                TitleEn = OptionalText(table, NotificationsLimits.TitleTextMaxLength),
                BodyAr = OptionalText(table, NotificationsLimits.BodyTextMaxLength),
                BodyEn = OptionalText(table, NotificationsLimits.BodyTextMaxLength),
                CreatedAt = table.Column<DateTimeOffset>(type: NotificationsSqlTypes.DateTimeOffset, nullable: false),
                ReadAt = OptionalTime(table),
                EmailState = Integer(table),
                SmsState = Integer(table),
                EmailSentAt = OptionalTime(table),
                SmsSentAt = OptionalTime(table),
            },
            constraints: table => table.PrimaryKey(NotificationsMigrationIds.NotificationsPrimaryKey, notification => notification.Id));

    private static void CreateTemplates(MigrationBuilder migrationBuilder) =>
        migrationBuilder.CreateTable(
            name: NotificationsTableNames.Templates,
            schema: NotificationsDbContext.SchemaName,
            columns: table => new
            {
                Key = table.Column<string>(
                    type: NotificationsSqlTypes.NVarChar(NotificationsLimits.TemplateKeyMaxLength),
                    maxLength: NotificationsLimits.TemplateKeyMaxLength,
                    nullable: false),
                Channel = Integer(table),
                Language = Integer(table),
                Subject = OptionalText(table, NotificationsLimits.SubjectMaxLength),
                Body = table.Column<string>(type: NotificationsSqlTypes.NVarCharMax, nullable: false),
            },
            constraints: table => table.PrimaryKey(
                NotificationsMigrationIds.TemplatesPrimaryKey,
                template => new { template.Key, template.Channel, template.Language }));

    private static void SeedTemplates(MigrationBuilder migrationBuilder)
    {
        foreach (var text in TemplateSeed.Rows.Select(NotificationTemplate.Define))
        {
            migrationBuilder.InsertData(
                table: NotificationsTableNames.Templates,
                schema: NotificationsDbContext.SchemaName,
                columns: TemplateColumns,
                values: [text.Key, (int)text.Channel, (int)text.Language, text.Subject, text.Body]);
        }
    }

    private static OperationBuilder<AddColumnOperation> Integer(ColumnsBuilder table) =>
        table.Column<int>(type: NotificationsSqlTypes.Int32Column, nullable: false);

    private static OperationBuilder<AddColumnOperation> OptionalTime(ColumnsBuilder table) =>
        table.Column<DateTimeOffset>(type: NotificationsSqlTypes.DateTimeOffset, nullable: true);

    private static OperationBuilder<AddColumnOperation> OptionalText(ColumnsBuilder table, int maxLength) =>
        table.Column<string>(type: NotificationsSqlTypes.NVarChar(maxLength), maxLength: maxLength, nullable: true);
}
