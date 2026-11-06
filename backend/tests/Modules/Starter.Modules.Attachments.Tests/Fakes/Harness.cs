using Microsoft.Extensions.Time.Testing;
using Starter.Application.Results;
using Starter.Modules.Attachments.Application;
using Starter.Modules.Attachments.Domain;

namespace Starter.Modules.Attachments.Tests.Fakes;

/// <summary>Every use case over one set of fakes: the demo type seeded, the demo record viewable and changeable by <see cref="TestData.User"/>.</summary>
internal sealed class Harness
{
    public static readonly DateTimeOffset Now = new(2026, 3, 4, 5, 6, 7, TimeSpan.Zero);

    public Harness(Guid? user = null, bool anonymous = false)
    {
        Store = new FakeAttachmentStore();
        Store.Types.Add(DemoType);
        Store.Types.Add(TestData.OptionalType());
        Cleanup = new FakeFileCleanup(Store);
        CurrentUser = new FakeCurrentUser(anonymous ? null : user ?? TestData.User);
        Clock = new FakeTimeProvider(Now);
        Records = new AttachableRecords([Access, new FakeRecordAccess(TestData.OtherRecordType)]);
    }

    public ServiceAttachment DemoType { get; } = TestData.DemoType();

    public FakeAttachmentStore Store { get; }

    public FakeRecordAccess Access { get; } = new();

    public FakeReceiptVerifier Verifier { get; } = new();

    public FakeStorageClient Storage { get; } = new();

    public FakeFileCleanup Cleanup { get; }

    public FakeTicketIssuer Issuer { get; } = new();

    public FakeCurrentUser CurrentUser { get; }

    public FakeTimeProvider Clock { get; }

    public AttachableRecords Records { get; }

    public AttachmentActor Actor => new(CurrentUser, Clock);

    public AttachmentTargets Targets => new(Records, Store);

    public IssueUploadTicket IssueUploadTicket => new(new IssueUploadTicketCommandValidator(), Targets, Issuer, TestData.Scopes, Actor);

    public LinkUploadedFile LinkUploadedFile => new(new LinkUploadedFileCommandValidator(), Targets, new ScopedReceipts(Verifier, TestData.Scopes), Store, Actor);

    public RemoveAttachment RemoveAttachment => new(Records, Store, Cleanup, Actor);

    public DownloadAttachment DownloadAttachment => new(Records, Store, Storage);

    public ListRecordAttachments ListRecordAttachments => new(Records, Store);

    public ListAttachmentTypes ListAttachmentTypes => new(Records, Store);

    /// <summary>Registers a receipt the verifier accepts, issued for the demo type, the demo record and <paramref name="scopeUser"/>.</summary>
    public string Receipt(string fileId = "fileIdAlpha00000000001", string fileName = "report.pdf", Guid? scopeUser = null, long recordId = TestData.RecordId)
    {
        var receipt = "r1.test." + fileId + "." + recordId.ToString(System.Globalization.CultureInfo.InvariantCulture) + "." + (scopeUser ?? TestData.User).ToString("N");
        Verifier.Receipts[receipt] = new VerifiedUpload(TestData.ScopeFor(DemoType, scopeUser ?? TestData.User, recordId), TestData.File(fileId, fileName));
        return receipt;
    }

    /// <summary>A live demo attachment stored earlier.</summary>
    public RecordAttachment StoredDemo(string fileId) => Store.Stored(TestData.Link(DemoType, fileId));

    public static void AssertRefused<T>(Outcome<T> outcome, RefusalCategory category, string code)
    {
        Assert.False(outcome.IsSuccess);
        Assert.Equal((category, code), (outcome.Refusal!.Category, outcome.Refusal.Code));
    }
}
