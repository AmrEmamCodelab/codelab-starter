using Starter.Application.Results;
using Starter.Application.Validation;
using Starter.Domain.Attachments;
using Starter.Domain.Exceptions;
using Starter.Modules.Attachments.Application;
using Starter.Modules.Attachments.Tests.Fakes;

namespace Starter.Modules.Attachments.Tests.Application;

public sealed class IssueUploadTicketTests
{
    private readonly Harness _harness = new();

    [Fact]
    public async Task A_ticket_is_scoped_to_the_record_type_and_user()
    {
        _harness.StoredDemo("fileIdBravo00000000002");

        var outcome = await Issue(AttachmentTypeKeys.DemoDocument);

        Assert.True(outcome.IsSuccess);
        Assert.Equal("attachments-demo|pid~attachable-records~1|demo-document|11111111-2222-3333-4444-555555555555", Assert.Single(_harness.Issuer.Scopes));
        Assert.Equal(("t1.test.1", FakeTicketIssuer.Header, FakeTicketIssuer.Url, FakeTicketIssuer.ExpiresAt), (outcome.Value.Ticket, outcome.Value.TicketHeader, outcome.Value.UploadUrl, outcome.Value.ExpiresAt));
        Assert.Equal([".pdf", ".png"], outcome.Value.AllowedExtensions);
        Assert.Equal(1, outcome.Value.MaxFileCount);
    }

    [Fact]
    public async Task A_full_type_gets_no_ticket()
    {
        _harness.StoredDemo("fileIdBravo00000000002");
        _harness.StoredDemo("fileIdCharlie000000003");

        var outcome = await Issue(AttachmentTypeKeys.DemoDocument);

        Harness.AssertRefused(outcome, RefusalCategory.Conflict, AttachmentsErrorCodes.MaxFilesReached);
        Assert.Empty(_harness.Issuer.Scopes);
    }

    [Fact]
    public async Task A_removed_attachment_frees_its_slot()
    {
        var removed = _harness.StoredDemo("fileIdBravo00000000002");
        _harness.StoredDemo("fileIdCharlie000000003");
        await _harness.RemoveAttachment.ExecuteAsync(removed.Id, CancellationToken.None);

        var outcome = await Issue(AttachmentTypeKeys.DemoDocument);

        Assert.True(outcome.IsSuccess);
        Assert.Equal(1, outcome.Value.MaxFileCount);
    }

    [Theory]
    [InlineData("missing-type")]
    [InlineData("other-type-key")]
    public async Task An_unknown_type_gets_no_ticket(string typeKey)
    {
        var outcome = await Issue(typeKey);

        Harness.AssertRefused(outcome, RefusalCategory.NotFound, AttachmentsErrorCodes.TypeNotFound);
    }

    [Fact]
    public async Task A_type_of_another_record_type_is_not_found()
    {
        var outcome = await _harness.IssueUploadTicket.ExecuteAsync(
            new AttachableRecord(TestData.OtherRecordType, TestData.RecordId), new IssueUploadTicketCommand(AttachmentTypeKeys.DemoDocument), CancellationToken.None);

        Harness.AssertRefused(outcome, RefusalCategory.NotFound, AttachmentsErrorCodes.TypeNotFound);
    }

    [Fact]
    public async Task A_record_the_user_may_not_change_gets_no_ticket()
    {
        _harness.Access.CanChange = false;

        var outcome = await Issue(AttachmentTypeKeys.DemoDocument);

        Harness.AssertRefused(outcome, RefusalCategory.NotFound, AttachmentsErrorCodes.NotFound);
        Assert.Empty(_harness.Issuer.Scopes);
    }

    [Fact]
    public async Task A_missing_record_gets_no_ticket()
    {
        var outcome = await _harness.IssueUploadTicket.ExecuteAsync(
            new AttachableRecord(AttachableRecordTypes.Demo, TestData.RecordId + 1), new IssueUploadTicketCommand(AttachmentTypeKeys.DemoDocument), CancellationToken.None);

        Harness.AssertRefused(outcome, RefusalCategory.NotFound, AttachmentsErrorCodes.NotFound);
    }

    [Theory]
    [InlineData(null, ValidationMessages.Required)]
    [InlineData("  ", ValidationMessages.Required)]
    [InlineData("Demo_Document", ValidationMessages.InvalidFormat)]
    public async Task A_malformed_type_key_is_invalid(string? typeKey, string message)
    {
        var outcome = await Issue(typeKey);

        Harness.AssertRefused(outcome, RefusalCategory.Invalid, PlatformErrorCodes.Validation);
        Assert.Equal([message], outcome.Refusal!.FieldErrors["typeKey"]);
    }

    [Fact]
    public async Task An_anonymous_caller_is_denied()
    {
        var anonymous = new Harness(anonymous: true);

        await Assert.ThrowsAsync<AccessDeniedException>(() =>
            anonymous.IssueUploadTicket.ExecuteAsync(TestData.Demo, new IssueUploadTicketCommand(AttachmentTypeKeys.DemoDocument), CancellationToken.None));
    }

    private Task<Outcome<UploadTicketView>> Issue(string? typeKey) =>
        _harness.IssueUploadTicket.ExecuteAsync(TestData.Demo, new IssueUploadTicketCommand(typeKey), CancellationToken.None);
}

public sealed class RemoveAttachmentTests
{
    private readonly Harness _harness = new();

    [Fact]
    public async Task A_removal_is_soft_saved_then_enqueues_the_cleanup()
    {
        var attachment = _harness.StoredDemo("fileIdBravo00000000002");

        var outcome = await _harness.RemoveAttachment.ExecuteAsync(attachment.Id, CancellationToken.None);

        Assert.True(outcome.IsSuccess);
        Assert.Equal((Harness.Now, TestData.User), (attachment.RemovedAt!.Value, attachment.RemovedBySubject!.Value));
        Assert.Contains(attachment, _harness.Store.Attachments);
        Assert.Equal([("fileIdBravo00000000002", 1)], _harness.Cleanup.Enqueued);
    }

    [Fact]
    public async Task An_already_removed_attachment_is_not_found_and_not_cleaned_twice()
    {
        var attachment = _harness.StoredDemo("fileIdBravo00000000002");
        await _harness.RemoveAttachment.ExecuteAsync(attachment.Id, CancellationToken.None);

        var outcome = await _harness.RemoveAttachment.ExecuteAsync(attachment.Id, CancellationToken.None);

        Harness.AssertRefused(outcome, RefusalCategory.NotFound, AttachmentsErrorCodes.NotFound);
        Assert.Single(_harness.Cleanup.Enqueued);
    }

    [Fact]
    public async Task An_unknown_attachment_is_not_found()
    {
        var outcome = await _harness.RemoveAttachment.ExecuteAsync(12345, CancellationToken.None);

        Harness.AssertRefused(outcome, RefusalCategory.NotFound, AttachmentsErrorCodes.NotFound);
    }

    [Fact]
    public async Task A_record_the_user_may_not_change_keeps_its_attachment()
    {
        var attachment = _harness.StoredDemo("fileIdBravo00000000002");
        _harness.Access.CanChange = false;

        var outcome = await _harness.RemoveAttachment.ExecuteAsync(attachment.Id, CancellationToken.None);

        Harness.AssertRefused(outcome, RefusalCategory.NotFound, AttachmentsErrorCodes.NotFound);
        Assert.True(attachment.IsLive);
        Assert.Empty(_harness.Cleanup.Enqueued);
        Assert.Equal(0, _harness.Store.Saves);
    }

    [Fact]
    public async Task An_attachment_of_an_unregistered_record_type_is_not_found()
    {
        var orphan = _harness.Store.Stored(TestData.Link(_harness.DemoType, "fileIdBravo00000000002", recordType: "removed-module"));

        var outcome = await _harness.RemoveAttachment.ExecuteAsync(orphan.Id, CancellationToken.None);

        Harness.AssertRefused(outcome, RefusalCategory.NotFound, AttachmentsErrorCodes.NotFound);
    }
}

public sealed class DownloadAttachmentTests
{
    private readonly Harness _harness = new();

    [Fact]
    public async Task The_bytes_come_with_the_recorded_name_and_type()
    {
        var attachment = _harness.StoredDemo("fileIdBravo00000000002");
        _harness.Storage.Files["fileIdBravo00000000002"] = [1, 2, 3];

        var outcome = await _harness.DownloadAttachment.ExecuteAsync(attachment.Id, CancellationToken.None);

        Assert.True(outcome.IsSuccess);
        await using var content = outcome.Value.Content;
        using var copy = new MemoryStream();
        await content.CopyToAsync(copy);
        Assert.Equal([1, 2, 3], copy.ToArray());
        Assert.Equal(("report.pdf", "application/pdf", 1024L), (outcome.Value.FileName, outcome.Value.ContentType, outcome.Value.SizeBytes));
        Assert.Equal([(RecordAccessKind.View, TestData.RecordId)], _harness.Access.Asked);
    }

    [Fact]
    public async Task A_record_the_user_may_not_view_is_not_found_and_the_service_is_not_called()
    {
        var attachment = _harness.StoredDemo("fileIdBravo00000000002");
        _harness.Access.CanView = false;
        _harness.Storage.Unavailable = true;

        var outcome = await _harness.DownloadAttachment.ExecuteAsync(attachment.Id, CancellationToken.None);

        Harness.AssertRefused(outcome, RefusalCategory.NotFound, AttachmentsErrorCodes.NotFound);
    }

    [Fact]
    public async Task A_removed_attachment_is_not_found()
    {
        var attachment = _harness.StoredDemo("fileIdBravo00000000002");
        _harness.Storage.Files["fileIdBravo00000000002"] = [1];
        await _harness.RemoveAttachment.ExecuteAsync(attachment.Id, CancellationToken.None);

        var outcome = await _harness.DownloadAttachment.ExecuteAsync(attachment.Id, CancellationToken.None);

        Harness.AssertRefused(outcome, RefusalCategory.NotFound, AttachmentsErrorCodes.NotFound);
    }

    [Fact]
    public async Task Bytes_the_service_no_longer_has_are_not_found()
    {
        var attachment = _harness.StoredDemo("fileIdBravo00000000002");

        var outcome = await _harness.DownloadAttachment.ExecuteAsync(attachment.Id, CancellationToken.None);

        Harness.AssertRefused(outcome, RefusalCategory.NotFound, AttachmentsErrorCodes.NotFound);
    }

    [Fact]
    public async Task A_service_outage_propagates_as_storage_unavailable()
    {
        var attachment = _harness.StoredDemo("fileIdBravo00000000002");
        _harness.Storage.Unavailable = true;

        var failure = await Assert.ThrowsAsync<AttachmentStorageUnavailableException>(() => _harness.DownloadAttachment.ExecuteAsync(attachment.Id, CancellationToken.None));

        Assert.Equal(AttachmentStorageUnavailableException.UpstreamName, failure.Upstream);
    }
}

public sealed class ListRecordAttachmentsTests
{
    private readonly Harness _harness = new();

    [Fact]
    public async Task Live_attachments_and_missing_required_keys_are_listed()
    {
        var live = _harness.StoredDemo("fileIdBravo00000000002");
        var removed = _harness.StoredDemo("fileIdCharlie000000003");
        await _harness.RemoveAttachment.ExecuteAsync(removed.Id, CancellationToken.None);

        var outcome = await _harness.ListRecordAttachments.ExecuteAsync(TestData.Demo, CancellationToken.None);

        Assert.True(outcome.IsSuccess);
        var item = Assert.Single(outcome.Value.Items);
        Assert.Equal((live.Id, AttachmentTypeKeys.DemoDocument, "report.pdf"), (item.Id, item.TypeKey, item.FileName));
        Assert.Empty(outcome.Value.MissingRequiredTypeKeys);
    }

    [Fact]
    public async Task A_record_without_its_required_file_reports_it_missing()
    {
        var outcome = await _harness.ListRecordAttachments.ExecuteAsync(TestData.Demo, CancellationToken.None);

        Assert.Empty(outcome.Value.Items);
        Assert.Equal([AttachmentTypeKeys.DemoDocument], outcome.Value.MissingRequiredTypeKeys);
    }

    [Fact]
    public async Task An_attachment_of_a_retired_type_is_still_listed_under_its_key()
    {
        _harness.StoredDemo("fileIdBravo00000000002");
        _harness.DemoType.SetActive(false);

        var outcome = await _harness.ListRecordAttachments.ExecuteAsync(TestData.Demo, CancellationToken.None);

        Assert.Equal(AttachmentTypeKeys.DemoDocument, Assert.Single(outcome.Value.Items).TypeKey);
        Assert.Empty(outcome.Value.MissingRequiredTypeKeys);
    }

    [Fact]
    public async Task A_record_the_user_may_not_view_is_not_found()
    {
        _harness.Access.CanView = false;

        var outcome = await _harness.ListRecordAttachments.ExecuteAsync(TestData.Demo, CancellationToken.None);

        Harness.AssertRefused(outcome, RefusalCategory.NotFound, AttachmentsErrorCodes.NotFound);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("unknown-records")]
    [InlineData("Not Kebab")]
    public async Task An_unknown_record_type_is_refused(string? recordType)
    {
        var outcome = await _harness.ListRecordAttachments.ExecuteAsync(new AttachableRecord(recordType, TestData.RecordId), CancellationToken.None);

        Harness.AssertRefused(outcome, RefusalCategory.NotFound, AttachmentsErrorCodes.RecordTypeUnknown);
        Assert.Empty(_harness.Access.Asked);
    }
}

public sealed class ListAttachmentTypesTests
{
    private readonly Harness _harness = new();

    [Fact]
    public async Task The_active_types_are_listed_by_key()
    {
        var outcome = await _harness.ListAttachmentTypes.ExecuteAsync(AttachableRecordTypes.Demo, CancellationToken.None);

        Assert.Equal([AttachmentTypeKeys.DemoDocument, TestData.OptionalTypeKey], outcome.Value.Select(type => type.Key));
        var demo = outcome.Value[0];
        Assert.Equal(("مستند تجريبي", "Demo document", true, 2), (demo.NameAr, demo.NameEn, demo.IsRequired, demo.MaxFiles));
        Assert.Equal([".pdf", ".png"], demo.AllowedExtensions);
    }

    [Fact]
    public async Task A_retired_type_is_not_offered()
    {
        _harness.DemoType.SetActive(false);

        var outcome = await _harness.ListAttachmentTypes.ExecuteAsync(AttachableRecordTypes.Demo, CancellationToken.None);

        Assert.Equal([TestData.OptionalTypeKey], outcome.Value.Select(type => type.Key));
    }

    [Fact]
    public async Task An_unknown_record_type_is_refused()
    {
        var outcome = await _harness.ListAttachmentTypes.ExecuteAsync("unknown-records", CancellationToken.None);

        Harness.AssertRefused(outcome, RefusalCategory.NotFound, AttachmentsErrorCodes.RecordTypeUnknown);
    }

    [Fact]
    public async Task A_known_record_type_without_types_answers_an_empty_list()
    {
        var outcome = await _harness.ListAttachmentTypes.ExecuteAsync(TestData.OtherRecordType, CancellationToken.None);

        Assert.True(outcome.IsSuccess);
        Assert.Empty(outcome.Value);
    }
}
