using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Operations;
using Microsoft.EntityFrameworkCore.Migrations.Operations.Builders;
using Starter.Modules.Portal.Domain;

namespace Starter.Modules.Portal.Infrastructure.Persistence.Migrations;

/// <summary>
/// The Portal schema: the contact messages (unique reference number), one header row per content page, and the
/// content sections with the seeded starter text (<see cref="ContentSeed"/>). Written in the reshaped migration style
/// of the other modules; <see cref="PortalMigrationModel"/> is its target model and the snapshot, and the model test
/// proves the context has no pending change against it. See MODULE.md.
/// </summary>
[DbContext(typeof(PortalDbContext))]
[Migration(PortalMigrationIds.InitialCreate)]
public sealed class InitialCreate : Migration
{
    private static readonly string[] SectionColumns =
    [
        nameof(ContentSection.Page), nameof(ContentSection.TitleAr), nameof(ContentSection.TitleEn),
        nameof(ContentSection.BodyAr), nameof(ContentSection.BodyEn), nameof(ContentSection.SortOrder),
    ];

    private static readonly string[] PageColumns = [nameof(ContentPage.Page)];

    private static readonly string[] SectionIndexColumns = [nameof(ContentSection.Page), nameof(ContentSection.SortOrder)];

    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        ArgumentNullException.ThrowIfNull(migrationBuilder);
        migrationBuilder.EnsureSchema(name: PortalDbContext.SchemaName);
        CreateContactMessages(migrationBuilder);
        CreateContentPages(migrationBuilder);
        CreateContentSections(migrationBuilder);
        migrationBuilder.CreateIndex(
            name: PortalMigrationIds.ReferenceNumberIndex,
            schema: PortalDbContext.SchemaName,
            table: PortalTableNames.ContactMessages,
            column: nameof(ContactMessage.ReferenceNumber),
            unique: true);
        migrationBuilder.CreateIndex(
            name: PortalMigrationIds.SectionsPageIndex,
            schema: PortalDbContext.SchemaName,
            table: PortalTableNames.ContentSections,
            columns: SectionIndexColumns);
        SeedContent(migrationBuilder);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        ArgumentNullException.ThrowIfNull(migrationBuilder);
        migrationBuilder.DropTable(name: PortalTableNames.ContentSections, schema: PortalDbContext.SchemaName);
        migrationBuilder.DropTable(name: PortalTableNames.ContentPages, schema: PortalDbContext.SchemaName);
        migrationBuilder.DropTable(name: PortalTableNames.ContactMessages, schema: PortalDbContext.SchemaName);
    }

    /// <inheritdoc />
    protected override void BuildTargetModel(ModelBuilder modelBuilder) => PortalMigrationModel.Build(modelBuilder);

    private static void CreateContactMessages(MigrationBuilder migrationBuilder) =>
        migrationBuilder.CreateTable(
            name: PortalTableNames.ContactMessages,
            schema: PortalDbContext.SchemaName,
            columns: table => new
            {
                Id = table.Column<long>(type: PortalSqlTypes.BigInt, nullable: false)
                    .Annotation(PortalSqlTypes.IdentityAnnotation, PortalSqlTypes.IdentitySeedIncrement),
                ReferenceNumber = Text(table, PortalLimits.ReferenceNumberMaxLength),
                FullName = Text(table, PortalLimits.FullNameMaxLength),
                Email = Text(table, PortalLimits.EmailMaxLength),
                TopicId = Integer(table),
                Message = Text(table, PortalLimits.MessageMaxLength),
                Language = Integer(table),
                ReceivedAt = table.Column<DateTimeOffset>(type: PortalSqlTypes.DateTimeOffset, nullable: false),
            },
            constraints: table => table.PrimaryKey(PortalMigrationIds.ContactMessagesPrimaryKey, message => message.Id));

    private static void CreateContentPages(MigrationBuilder migrationBuilder) =>
        migrationBuilder.CreateTable(
            name: PortalTableNames.ContentPages,
            schema: PortalDbContext.SchemaName,
            columns: table => new
            {
                Page = Integer(table),
                UpdatedAt = table.Column<DateTimeOffset>(type: PortalSqlTypes.DateTimeOffset, nullable: true),
            },
            constraints: table => table.PrimaryKey(PortalMigrationIds.ContentPagesPrimaryKey, page => page.Page));

    private static void CreateContentSections(MigrationBuilder migrationBuilder) =>
        migrationBuilder.CreateTable(
            name: PortalTableNames.ContentSections,
            schema: PortalDbContext.SchemaName,
            columns: table => new
            {
                Id = table.Column<long>(type: PortalSqlTypes.BigInt, nullable: false)
                    .Annotation(PortalSqlTypes.IdentityAnnotation, PortalSqlTypes.IdentitySeedIncrement),
                Page = Integer(table),
                TitleAr = Text(table, PortalLimits.SectionTitleMaxLength),
                TitleEn = Text(table, PortalLimits.SectionTitleMaxLength),
                BodyAr = LongText(table),
                BodyEn = LongText(table),
                SortOrder = Integer(table),
            },
            constraints: table =>
            {
                table.PrimaryKey(PortalMigrationIds.ContentSectionsPrimaryKey, section => section.Id);
                table.ForeignKey(
                    name: PortalMigrationIds.SectionsPageForeignKey,
                    column: section => section.Page,
                    principalSchema: PortalDbContext.SchemaName,
                    principalTable: PortalTableNames.ContentPages,
                    principalColumn: nameof(ContentPage.Page),
                    onDelete: ReferentialAction.Restrict);
            });

    private static void SeedContent(MigrationBuilder migrationBuilder)
    {
        foreach (var page in PortalPages.All)
        {
            migrationBuilder.InsertData(
                table: PortalTableNames.ContentPages,
                schema: PortalDbContext.SchemaName,
                columns: PageColumns,
                values: [(int)page]);
        }

        foreach (var section in ContentSeed.Sections)
        {
            migrationBuilder.InsertData(
                table: PortalTableNames.ContentSections,
                schema: PortalDbContext.SchemaName,
                columns: SectionColumns,
                values: [(int)section.Page, section.Text.TitleAr, section.Text.TitleEn, section.Text.BodyAr, section.Text.BodyEn, section.SortOrder]);
        }
    }

    private static OperationBuilder<AddColumnOperation> Integer(ColumnsBuilder table) =>
        table.Column<int>(type: PortalSqlTypes.Int32Column, nullable: false);

    private static OperationBuilder<AddColumnOperation> Text(ColumnsBuilder table, int maxLength) =>
        table.Column<string>(type: PortalSqlTypes.NVarChar(maxLength), maxLength: maxLength, nullable: false);

    private static OperationBuilder<AddColumnOperation> LongText(ColumnsBuilder table) =>
        table.Column<string>(type: PortalSqlTypes.NVarCharMax, maxLength: PortalLimits.SectionBodyMaxLength, nullable: false);
}
