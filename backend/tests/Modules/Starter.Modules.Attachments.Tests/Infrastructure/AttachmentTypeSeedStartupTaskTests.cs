using Codelab.Security.Uploads;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Starter.Application.Attachments;
using Starter.Infrastructure.Startup;
using Starter.Modules.Attachments.Application;
using Starter.Modules.Attachments.Infrastructure.Persistence;
using Starter.Modules.Attachments.Infrastructure.Persistence.Migrations;
using Starter.Modules.Attachments.Infrastructure.Startup;

namespace Starter.Modules.Attachments.Tests.Infrastructure;

/// <summary>
/// The start-up task's own decisions, without a database: the context points at a server that is never opened, so a
/// test passes only when the task decides before any query. The writes are proven on real SQL by the ServiceRequests
/// HTTP tests (the sample's declared types after migrate and their submit interlock).
/// </summary>
public sealed class AttachmentTypeSeedStartupTaskTests
{
    private static AttachmentTypeDefinition Declared { get; } = new(
        Id: 30, Key: "startup-file", RecordType: "startup-records", NameAr: "ملف", NameEn: "File",
        IsRequired: false, MaxFiles: 1, AllowedExtensions: [".pdf"]);

    [Fact]
    public void The_task_runs_with_the_reference_data()
    {
        Assert.Equal(StartupPhase.ReferenceData, Task(new AttachmentTypeDeclarations([], AttachmentTypeSeed.Rows), new UploadSafetyOptions()).Phase);
    }

    [Fact]
    public async Task A_declared_extension_outside_the_upload_allowlist_stops_the_start_before_anything_is_written()
    {
        var declarations = new AttachmentTypeDeclarations([new Source(Declared with { AllowedExtensions = [".pdf", ".exe"] })], AttachmentTypeSeed.Rows);

        var refused = await Assert.ThrowsAsync<InvalidOperationException>(() => Task(declarations, new UploadSafetyOptions()).RunAsync(CancellationToken.None));

        Assert.Contains("startup-file .exe", refused.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Without_declared_types_the_synchronizer_never_touches_the_database()
    {
        using var context = NeverOpenedContext();
        var synchronizer = new AttachmentTypeSynchronizer(context, new AttachmentTypeDeclarations([], AttachmentTypeSeed.Rows));

        var report = await synchronizer.SynchronizeAsync(CancellationToken.None);

        Assert.Equal(new AttachmentTypeSeedReport(Added: 0, Refreshed: 0, Unchanged: 0), report);
    }

    private static AttachmentTypeSeedStartupTask Task(AttachmentTypeDeclarations declarations, UploadSafetyOptions uploads) => new(
        new AttachmentTypeSynchronizer(NeverOpenedContext(), declarations),
        declarations,
        Options.Create(uploads),
        NullLogger<AttachmentTypeSeedStartupTask>.Instance);

    private static AttachmentsDbContext NeverOpenedContext() => new(new DbContextOptionsBuilder<AttachmentsDbContext>()
        .UseSqlServer("Server=.;Database=NeverOpened")
        .Options);

    private sealed class Source(AttachmentTypeDefinition type) : IAttachmentTypeSeedSource
    {
        public IEnumerable<AttachmentTypeDefinition> Types => [type];
    }
}
