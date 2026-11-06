using Codelab.Security;
using Codelab.Security.Uploads;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Starter.Domain.Attachments;
using Starter.Integration.Tests.Support;
using Starter.Modules.Attachments.Domain;
using Starter.Modules.Attachments.Infrastructure.Persistence;
using Starter.Modules.Attachments.Infrastructure.Persistence.Migrations;
using Starter.Modules.Attachments.Infrastructure.Security;
using Starter.Modules.Attachments.Infrastructure.Startup;

namespace Starter.Integration.Tests.Attachments.Persistence;

/// <summary>
/// Module 13 on real SQL Server: the InitialCreate migration applies in schema <c>attachments</c> with its own history,
/// the Development demo type is seeded, links and soft removals persist through <see cref="EfAttachmentStore"/>, a file
/// id links once, and the type foreign key is Restrict (a type with links cannot be deleted; a link to no type is
/// refused). Each test uses its own record id and file ids on the shared collection database.
/// </summary>
[Collection(AttachmentsPersistenceGroup.Name)]
public sealed class AttachmentsPersistenceTests(AttachmentsDatabase database)
{
    /// <summary>SQL Server's error number for a statement that conflicts with a constraint (here the FK).</summary>
    private const int ConstraintConflict = 547;

    /// <summary>SQL Server's error number for a duplicate key in a unique index.</summary>
    private const int DuplicateKey = 2601;

    private const string Sha256 = "0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef";
    private const string TypeTable = "attachments.ServiceAttachments";

    private static readonly Guid Uploader = Guid.Parse("11111111-2222-3333-4444-555555555555");
    private static readonly Guid Remover = Guid.Parse("99999999-8888-7777-6666-555555555555");
    private static readonly DateTimeOffset UploadedAt = new(2026, 11, 6, 9, 0, 0, TimeSpan.FromHours(3));

    [Fact]
    public async Task The_migration_applied_in_the_attachments_schema_with_its_own_history()
    {
        await using var context = database.NewContext();

        Assert.Equal([AttachmentsMigrationIds.InitialCreate], await context.Database.GetAppliedMigrationsAsync());
        Assert.Empty(await context.Database.GetPendingMigrationsAsync());
        var tables = await TestSqlServer.ReadStringsAsync(
            database.Name,
            "SELECT [s].[name] + '.' + [t].[name] FROM sys.tables [t] JOIN sys.schemas [s] ON [s].[schema_id] = [t].[schema_id] ORDER BY 1",
            new Dictionary<string, object>());
        Assert.Equal(["attachments.__EFMigrationsHistory", "attachments.RecordAttachments", "attachments.ServiceAttachments"], tables);
    }

    [Fact]
    public async Task The_Development_demo_type_is_seeded()
    {
        await using var context = database.NewContext();
        var store = new EfAttachmentStore(context);

        var types = await store.ListTypesAsync(AttachableRecordTypes.Demo, CancellationToken.None);

        var demo = Assert.Single(types);
        Assert.Equal(AttachmentTypeIds.DemoDocument, demo.Id);
        Assert.Equal(AttachmentTypeKeys.DemoDocument, demo.Key);
        Assert.Equal("مستند تجريبي", demo.NameAr);
        Assert.Equal("Demo document", demo.NameEn);
        Assert.True(demo.IsRequired);
        Assert.True(demo.IsActive);
        Assert.Equal(AttachmentTypeSeed.DemoDocumentMaxFiles, demo.MaxFiles);
        Assert.Equal([".pdf", ".png", ".jpg", ".jpeg"], demo.AllowedExtensions);
        Assert.Equal(demo.Id, (await store.FindTypeAsync(AttachableRecordTypes.Demo, AttachmentTypeKeys.DemoDocument, CancellationToken.None))?.Id);
        Assert.Null(await store.FindTypeAsync("other-records", AttachmentTypeKeys.DemoDocument, CancellationToken.None));
    }

    [Fact]
    public async Task A_link_and_its_soft_removal_are_persisted()
    {
        const long recordId = 1001;
        var fileId = NewFileId();
        long attachmentId;
        await using (var context = database.NewContext())
        {
            var store = new EfAttachmentStore(context);
            store.Add(Link(recordId, fileId));
            await store.SaveChangesAsync(CancellationToken.None);
            attachmentId = Assert.Single(await store.ListLiveAsync(AttachableRecordTypes.Demo, recordId, CancellationToken.None)).Id;
        }

        await using (var context = database.NewContext())
        {
            var store = new EfAttachmentStore(context);
            var tracked = await store.FindAsync(attachmentId, CancellationToken.None);
            Assert.NotNull(tracked);
            Assert.Equal(fileId, tracked.FileId);
            Assert.Equal(Sha256, tracked.Sha256Hex);
            Assert.Equal(UploadedAt, tracked.UploadedAt);
            Assert.True(tracked.Remove(new AttachmentStamp(Remover, UploadedAt.AddHours(1))));
            await store.SaveChangesAsync(CancellationToken.None);
        }

        await using (var context = database.NewContext())
        {
            var store = new EfAttachmentStore(context);
            Assert.Empty(await store.ListLiveAsync(AttachableRecordTypes.Demo, recordId, CancellationToken.None));
            var removed = await store.FindByFileIdAsync(fileId, CancellationToken.None);
            Assert.NotNull(removed);
            Assert.False(removed.IsLive);
            Assert.Equal(Remover, removed.RemovedBySubject);
            Assert.Equal(UploadedAt.AddHours(1), removed.RemovedAt);
        }
    }

    [Fact]
    public async Task Live_attachments_are_listed_per_record_in_upload_order()
    {
        const long recordId = 1002;
        var (first, second) = (NewFileId(), NewFileId());
        await using (var context = database.NewContext())
        {
            var store = new EfAttachmentStore(context);
            store.Add(Link(recordId, second, UploadedAt.AddMinutes(5)));
            store.Add(Link(recordId, first, UploadedAt));
            store.Add(Link(recordId + 1, NewFileId(), UploadedAt));
            await store.SaveChangesAsync(CancellationToken.None);
        }

        await using var check = database.NewContext();
        var live = await new EfAttachmentStore(check).ListLiveAsync(AttachableRecordTypes.Demo, recordId, CancellationToken.None);

        Assert.Equal([first, second], live.Select(attachment => attachment.FileId));
    }

    [Fact]
    public async Task A_file_id_links_only_once()
    {
        var fileId = NewFileId();
        await using (var context = database.NewContext())
        {
            context.Attachments.Add(Link(1003, fileId));
            await context.SaveChangesAsync();
        }

        await using var duplicate = database.NewContext();
        duplicate.Attachments.Add(Link(1004, fileId));
        var refused = await Assert.ThrowsAsync<DbUpdateException>(() => duplicate.SaveChangesAsync());

        Assert.Equal(DuplicateKey, Assert.IsType<SqlException>(refused.InnerException).Number);
    }

    [Fact]
    public async Task The_type_foreign_key_is_restrict_in_the_database()
    {
        var deleteAction = await TestSqlServer.ScalarAsync(
            database.Name,
            "SELECT [delete_referential_action_desc] FROM sys.foreign_keys WHERE [name] = @name",
            new Dictionary<string, object> { ["@name"] = AttachmentsMigrationIds.RecordAttachmentsTypeForeignKey });

        Assert.Equal("NO_ACTION", deleteAction); // SQL Server's form of Restrict: the delete is refused, nothing cascades
    }

    [Fact]
    public async Task Deleting_a_type_that_has_links_is_refused_by_SQL_and_both_rows_stay()
    {
        const long recordId = 1005;
        var fileId = NewFileId();
        await using (var context = database.NewContext())
        {
            context.Attachments.Add(Link(recordId, fileId));
            await context.SaveChangesAsync();
        }

        var refused = await Assert.ThrowsAsync<SqlException>(() => TestSqlServer.ExecuteAsync(
            database.Name,
            $"DELETE FROM {TypeTable} WHERE [Id] = @id",
            new Dictionary<string, object> { ["@id"] = AttachmentTypeIds.DemoDocument }));

        Assert.Equal(ConstraintConflict, refused.Number);
        await using var check = database.NewContext();
        Assert.True(await check.Types.AnyAsync(type => type.Id == AttachmentTypeIds.DemoDocument));
        Assert.True(await check.Attachments.AnyAsync(attachment => attachment.FileId == fileId));
    }

    [Fact]
    public async Task A_link_to_a_type_that_does_not_exist_is_refused_by_SQL()
    {
        await using var context = database.NewContext();
        context.Attachments.Add(RecordAttachment.Link(
            new AttachmentOwner(AttachableRecordTypes.Demo, 1006, ServiceAttachmentId: 9999),
            File(NewFileId()),
            new AttachmentStamp(Uploader, UploadedAt)));

        var refused = await Assert.ThrowsAsync<DbUpdateException>(() => context.SaveChangesAsync());

        Assert.Equal(ConstraintConflict, Assert.IsType<SqlException>(refused.InnerException).Number);
    }

    [Fact]
    public async Task The_startup_check_passes_on_the_seeded_catalog_and_fails_on_a_narrower_allowlist()
    {
        var protocol = SecurityProtocol.Create(SecurityProtocol.TestPrefix);
        var receipts = new ReceiptVerifier(Options.Create(new UploadSafetyOptions()), protocol);
        await using var context = database.NewContext();

        await Check(context, new UploadSafetyOptions(), receipts).RunAsync(CancellationToken.None);

        var narrow = new UploadSafetyOptions { AllowedExtensions = [".pdf"] };
        var refused = await Assert.ThrowsAsync<InvalidOperationException>(() => Check(context, narrow, receipts).RunAsync(CancellationToken.None));
        Assert.Contains($"{AttachmentTypeKeys.DemoDocument} .png", refused.Message, StringComparison.Ordinal);
    }

    private static AttachmentCatalogStartupCheck Check(AttachmentsDbContext context, UploadSafetyOptions uploads, ReceiptVerifier receipts) =>
        new(context, Options.Create(uploads), receipts, NullLogger<AttachmentCatalogStartupCheck>.Instance);

    private static RecordAttachment Link(long recordId, string fileId, DateTimeOffset? at = null) =>
        RecordAttachment.Link(
            new AttachmentOwner(AttachableRecordTypes.Demo, recordId, AttachmentTypeIds.DemoDocument),
            File(fileId),
            new AttachmentStamp(Uploader, at ?? UploadedAt));

    private static StoredFile File(string fileId) =>
        new(FileId: fileId, FileName: "report.pdf", SizeBytes: 2048, Sha256Hex: Sha256, ContentType: UploadContentTypes.Pdf);

    private static string NewFileId() => $"it{Guid.NewGuid():N}";
}
