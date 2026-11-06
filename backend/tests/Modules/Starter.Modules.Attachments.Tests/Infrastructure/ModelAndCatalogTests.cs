using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;
using Starter.Domain.Attachments;
using Starter.Modules.Attachments.Domain;
using Starter.Modules.Attachments.Infrastructure.Persistence;
using Starter.Modules.Attachments.Infrastructure.Persistence.Migrations;
using Starter.Modules.Attachments.Infrastructure.Startup;

namespace Starter.Modules.Attachments.Tests.Infrastructure;

public sealed class AttachmentsModelTests
{
    [Fact]
    public void The_model_lives_in_the_attachments_schema_and_matches_the_InitialCreate_migration()
    {
        using var context = NewContext();

        Assert.Equal(AttachmentsDbContext.SchemaName, context.Model.GetDefaultSchema());
        Assert.Equal(AttachmentsTableNames.ServiceAttachments, context.Model.FindEntityType(typeof(ServiceAttachment))!.GetTableName());
        Assert.Equal(AttachmentsTableNames.RecordAttachments, context.Model.FindEntityType(typeof(RecordAttachment))!.GetTableName());
        Assert.All(context.Model.GetEntityTypes(), entity => Assert.Equal(AttachmentsDbContext.SchemaName, entity.GetSchema()));
        Assert.Equal([AttachmentsMigrationIds.InitialCreate], context.Database.GetMigrations());
        Assert.False(context.Database.HasPendingModelChanges());
    }

    [Fact]
    public void An_attachment_points_at_its_type_with_a_Restrict_foreign_key()
    {
        using var context = NewContext();

        var foreignKey = Assert.Single(context.Model.FindEntityType(typeof(RecordAttachment))!.GetForeignKeys());

        Assert.Equal(typeof(ServiceAttachment), foreignKey.PrincipalEntityType.ClrType);
        Assert.Equal(DeleteBehavior.Restrict, foreignKey.DeleteBehavior);
    }

    [Fact]
    public void A_file_id_is_unique_and_a_type_key_is_unique()
    {
        using var context = NewContext();

        Assert.True(UniqueIndexOn(context.Model.FindEntityType(typeof(RecordAttachment))!, nameof(RecordAttachment.FileId)));
        Assert.True(UniqueIndexOn(context.Model.FindEntityType(typeof(ServiceAttachment))!, nameof(ServiceAttachment.Key)));
    }

    [Fact]
    public void Type_ids_are_never_generated_and_the_extensions_map_to_their_own_column()
    {
        using var context = NewContext();
        var type = context.Model.FindEntityType(typeof(ServiceAttachment))!;

        Assert.Equal(ValueGenerated.Never, type.FindProperty(nameof(ServiceAttachment.Id))!.ValueGenerated);
        Assert.Equal(AttachmentsTableNames.AllowedExtensionsColumn, type.FindProperty(AttachmentsTableNames.AllowedExtensionsProperty)!.GetColumnName());
        Assert.Null(type.FindProperty(nameof(ServiceAttachment.AllowedExtensions)));
    }

    [Fact]
    public void The_migration_script_creates_both_tables_the_restrict_key_and_seeds_the_demo_type()
    {
        using var context = NewContext();

        var script = context.GetService<IMigrator>().GenerateScript();

        Assert.Contains("CREATE TABLE [attachments].[ServiceAttachments]", script, StringComparison.Ordinal);
        Assert.Contains("CREATE TABLE [attachments].[RecordAttachments]", script, StringComparison.Ordinal);
        Assert.Contains($"CONSTRAINT [{AttachmentsMigrationIds.RecordAttachmentsTypeForeignKey}] FOREIGN KEY", script, StringComparison.Ordinal);
        Assert.DoesNotContain("ON DELETE CASCADE", script, StringComparison.Ordinal);
        Assert.Contains($"CREATE UNIQUE INDEX [{AttachmentsMigrationIds.RecordAttachmentsFileIdIndex}]", script, StringComparison.Ordinal);
        Assert.Contains("[attachments].[__EFMigrationsHistory]", script, StringComparison.Ordinal);
        Assert.Equal(AttachmentTypeSeed.Rows.Count, script.Split("INSERT INTO [attachments].[ServiceAttachments]").Length - 1);
        Assert.Contains("N'.pdf;.png;.jpg;.jpeg'", script, StringComparison.Ordinal);
        Assert.Contains("N'مستند تجريبي'", script, StringComparison.Ordinal);
    }

    [Fact]
    public void The_demo_seed_is_the_contract_type()
    {
        var demo = Assert.Single(AttachmentTypeSeed.Rows);

        Assert.Equal(AttachmentTypeIds.DemoDocument, demo.Id);
        Assert.Equal(AttachmentTypeKeys.DemoDocument, demo.Key);
        Assert.Equal(AttachableRecordTypes.Demo, demo.RecordType);
        Assert.True(demo.IsRequired);
        Assert.True(demo.IsActive);
        Assert.Equal(5, demo.MaxFiles);
        Assert.Equal([".pdf", ".png", ".jpg", ".jpeg"], demo.AllowedExtensions);
    }

    private static bool UniqueIndexOn(IEntityType entity, string property) =>
        entity.GetIndexes().Any(index => index.IsUnique && index.Properties.Select(column => column.Name).SequenceEqual([property]));

    private static AttachmentsDbContext NewContext() => new(new DbContextOptionsBuilder<AttachmentsDbContext>()
        .UseSqlServer("Server=.;Database=NeverOpened", sql => sql.MigrationsHistoryTable(HistoryRepository.DefaultTableName, AttachmentsDbContext.SchemaName))
        .Options);
}

public sealed class AttachmentCatalogCheckTests
{
    private static readonly UploadAllowlist Allowlist = new(Allowed: [".pdf", ".png", ".jpg", ".jpeg"], Blocked: [".exe"]);

    [Fact]
    public void The_seeded_catalog_fits_the_default_upload_allowlist()
    {
        var allowlist = AttachmentCatalogCheck.AllowlistOf(new Codelab.Security.Uploads.UploadSafetyOptions());

        AttachmentCatalogCheck.EnsureFits(AttachmentTypeSeed.Rows, allowlist);
    }

    [Fact]
    public void A_type_extension_outside_the_allowlist_fails_the_start_and_names_the_pair()
    {
        var narrow = new UploadAllowlist(Allowed: [".pdf"], Blocked: []);

        var refused = Assert.Throws<InvalidOperationException>(() => AttachmentCatalogCheck.EnsureFits(AttachmentTypeSeed.Rows, narrow));

        Assert.Contains("demo-document .png", refused.Message, StringComparison.Ordinal);
        Assert.Contains("demo-document .jpeg", refused.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("demo-document .pdf", refused.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void A_blocked_extension_fails_even_when_allowed()
    {
        var blocking = Allowlist with { Blocked = [".png"] };

        Assert.Throws<InvalidOperationException>(() => AttachmentCatalogCheck.EnsureFits(AttachmentTypeSeed.Rows, blocking));
    }

    [Fact]
    public void An_empty_catalog_fits()
    {
        AttachmentCatalogCheck.EnsureFits([], Allowlist);
    }
}
