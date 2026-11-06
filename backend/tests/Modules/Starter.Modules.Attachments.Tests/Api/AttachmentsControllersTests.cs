using System.Text;
using Codelab.Security.PublicIds;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Net.Http.Headers;
using Starter.Application.Results;
using Starter.Domain.Attachments;
using Starter.Modules.Attachments.Api;
using Starter.Modules.Attachments.Application;
using Starter.Modules.Attachments.Tests.Fakes;

namespace Starter.Modules.Attachments.Tests.Api;

/// <summary>
/// The controllers over the real use cases on in-memory fakes (<see cref="Harness"/>): each action maps its outcome to
/// the right status, body and problem+json code, with PublicIds on the wire.
/// </summary>
public sealed class AttachmentsControllersTests
{
    private const string FileId = "fileIdBravo00000000002";
    private static readonly byte[] FileBytes = Encoding.UTF8.GetBytes("%PDF-1.7 demo");
    private static readonly PublicId<AttachableRecordIds> DemoRecordId = PublicId<AttachableRecordIds>.From(TestData.RecordId);

    private readonly Harness _harness = new();

    [Fact]
    public async Task Types_answer_the_active_types_of_a_known_record_type()
    {
        var result = await Catalog().ListTypesAsync(AttachableRecordTypes.Demo, CancellationToken.None);

        var types = ResultProbe.ValueOf(result, StatusCodes.Status200OK);
        var demo = Assert.Single(types, type => type.Key == AttachmentTypeKeys.DemoDocument);
        Assert.True(demo.IsRequired);
        Assert.Equal(_harness.DemoType.MaxFiles, demo.MaxFiles);
        Assert.Equal(_harness.DemoType.AllowedExtensions, demo.AllowedExtensions);
    }

    [Theory]
    [InlineData("no-such-records")]
    [InlineData(null)]
    public async Task Types_of_an_unknown_record_type_are_404_with_the_code(string? recordType)
    {
        var result = await Catalog().ListTypesAsync(recordType, CancellationToken.None);

        Assert.Equal((StatusCodes.Status404NotFound, AttachmentsErrorCodes.RecordTypeUnknown), await ResultProbe.RefusalOf(result.Result));
    }

    [Fact]
    public async Task The_record_list_carries_public_ids_and_the_missing_required_types()
    {
        var stored = _harness.Store.Stored(TestData.Link(TestData.OptionalType(), FileId));

        var result = await Records().ListAsync(AttachableRecordTypes.Demo, DemoRecordId, CancellationToken.None);

        var list = ResultProbe.ValueOf(result, StatusCodes.Status200OK);
        var item = Assert.Single(list.Items);
        Assert.Equal(PublicId<AttachmentIds>.From(stored.Id), item.Id);
        Assert.Equal(TestData.OptionalTypeKey, item.TypeKey);
        Assert.Equal(stored.FileName, item.FileName);
        Assert.Equal([AttachmentTypeKeys.DemoDocument], list.MissingRequiredTypeKeys);
    }

    [Fact]
    public async Task A_hidden_record_list_is_404_not_found()
    {
        _harness.Access.CanView = false;

        var result = await Records().ListAsync(AttachableRecordTypes.Demo, DemoRecordId, CancellationToken.None);

        Assert.Equal((StatusCodes.Status404NotFound, AttachmentsErrorCodes.NotFound), await ResultProbe.RefusalOf(result.Result));
    }

    [Fact]
    public async Task A_ticket_answers_what_the_browser_needs_to_upload()
    {
        var result = await Records().IssueTicketAsync(
            AttachableRecordTypes.Demo, DemoRecordId, new IssueUploadTicketCommand(AttachmentTypeKeys.DemoDocument), CancellationToken.None);

        var ticket = ResultProbe.ValueOf(result, StatusCodes.Status200OK);
        Assert.Equal(FakeTicketIssuer.Header, ticket.TicketHeader);
        Assert.Equal(FakeTicketIssuer.Url, ticket.UploadUrl);
        Assert.Equal(FakeTicketIssuer.ExpiresAt, ticket.ExpiresAt);
        Assert.Equal(_harness.DemoType.MaxFiles, ticket.MaxFileCount);
        Assert.Equal([TestData.ScopeFor(_harness.DemoType, TestData.User)], _harness.Issuer.Scopes);
    }

    [Fact]
    public async Task A_ticket_without_a_type_key_is_400_validation()
    {
        var result = await Records().IssueTicketAsync(AttachableRecordTypes.Demo, DemoRecordId, new IssueUploadTicketCommand(null), CancellationToken.None);

        Assert.Equal((StatusCodes.Status400BadRequest, PlatformErrorCodes.Validation), await ResultProbe.RefusalOf(result.Result));
    }

    [Fact]
    public async Task A_ticket_for_a_full_type_is_409()
    {
        _harness.StoredDemo("fileIdCharlie000000003");
        _harness.StoredDemo("fileIdDelta00000000004");

        var result = await Records().IssueTicketAsync(
            AttachableRecordTypes.Demo, DemoRecordId, new IssueUploadTicketCommand(AttachmentTypeKeys.DemoDocument), CancellationToken.None);

        Assert.Equal((StatusCodes.Status409Conflict, AttachmentsErrorCodes.MaxFilesReached), await ResultProbe.RefusalOf(result.Result));
    }

    [Fact]
    public async Task A_valid_receipt_links_the_file_with_201()
    {
        var receipt = _harness.Receipt(fileId: FileId);

        var result = await Records().LinkAsync(
            AttachableRecordTypes.Demo, DemoRecordId, new LinkUploadedFileCommand(AttachmentTypeKeys.DemoDocument, receipt), CancellationToken.None);

        var item = ResultProbe.ValueOf(result, StatusCodes.Status201Created);
        var stored = Assert.Single(_harness.Store.Attachments);
        Assert.Equal(PublicId<AttachmentIds>.From(stored.Id), item.Id);
        Assert.Equal(AttachmentTypeKeys.DemoDocument, item.TypeKey);
        Assert.Equal(FileId, stored.FileId);
    }

    [Fact]
    public async Task A_tampered_receipt_is_400_receipt_invalid_and_links_nothing()
    {
        var tampered = _harness.Receipt(fileId: FileId) + "x";

        var result = await Records().LinkAsync(
            AttachableRecordTypes.Demo, DemoRecordId, new LinkUploadedFileCommand(AttachmentTypeKeys.DemoDocument, tampered), CancellationToken.None);

        Assert.Equal((StatusCodes.Status400BadRequest, AttachmentsErrorCodes.ReceiptInvalid), await ResultProbe.RefusalOf(result.Result));
        Assert.Empty(_harness.Store.Attachments);
    }

    [Fact]
    public async Task A_receipt_issued_to_another_user_is_400_receipt_invalid()
    {
        var foreign = _harness.Receipt(fileId: FileId, scopeUser: TestData.OtherUser);

        var result = await Records().LinkAsync(
            AttachableRecordTypes.Demo, DemoRecordId, new LinkUploadedFileCommand(AttachmentTypeKeys.DemoDocument, foreign), CancellationToken.None);

        Assert.Equal((StatusCodes.Status400BadRequest, AttachmentsErrorCodes.ReceiptInvalid), await ResultProbe.RefusalOf(result.Result));
    }

    [Fact]
    public async Task Remove_answers_204_and_enqueues_the_cleanup()
    {
        var stored = _harness.StoredDemo(FileId);

        var result = await Catalog().RemoveAsync(PublicId<AttachmentIds>.From(stored.Id), CancellationToken.None);

        Assert.IsType<NoContentResult>(result);
        Assert.False(stored.IsLive);
        Assert.Equal(FileId, Assert.Single(_harness.Cleanup.Enqueued).FileId);
    }

    [Fact]
    public async Task Removing_a_missing_attachment_is_404()
    {
        var result = await Catalog().RemoveAsync(PublicId<AttachmentIds>.From(long.MaxValue), CancellationToken.None);

        Assert.Equal((StatusCodes.Status404NotFound, AttachmentsErrorCodes.NotFound), await ResultProbe.RefusalOf(result));
        Assert.Empty(_harness.Cleanup.Enqueued);
    }

    [Fact]
    public async Task The_download_streams_the_bytes_as_an_attachment_with_nosniff()
    {
        var stored = _harness.StoredDemo(FileId);
        _harness.Storage.Files[FileId] = FileBytes;

        var result = await Content().DownloadAsync(PublicId<AttachmentIds>.From(stored.Id), CancellationToken.None);

        var file = Assert.IsType<AttachmentFileResult>(result);
        Assert.Equal(stored.ContentType, file.ContentType);
        Assert.Equal(stored.FileName, file.FileDownloadName);
        var written = await ResultProbe.ExecuteAsync(file);
        Assert.Equal(StatusCodes.Status200OK, written.Status);
        Assert.Equal(FileBytes, written.Body);
        Assert.Equal(AttachmentFileResult.NoSniff, written.Headers[HeaderNames.XContentTypeOptions].ToString());
        Assert.StartsWith("attachment;", written.Headers[HeaderNames.ContentDisposition].ToString(), StringComparison.Ordinal);
        Assert.Contains(stored.FileName, written.Headers[HeaderNames.ContentDisposition].ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_download_whose_bytes_are_gone_is_404()
    {
        var stored = _harness.StoredDemo(FileId);

        var result = await Content().DownloadAsync(PublicId<AttachmentIds>.From(stored.Id), CancellationToken.None);

        Assert.Equal((StatusCodes.Status404NotFound, AttachmentsErrorCodes.NotFound), await ResultProbe.RefusalOf(result));
    }

    [Fact]
    public async Task A_storage_outage_propagates_to_the_failure_classifier()
    {
        var stored = _harness.StoredDemo(FileId);
        _harness.Storage.Unavailable = true;

        await Assert.ThrowsAsync<AttachmentStorageUnavailableException>(
            () => Content().DownloadAsync(PublicId<AttachmentIds>.From(stored.Id), CancellationToken.None));
    }

    [Fact]
    public void The_demo_record_is_handed_out_while_its_record_type_is_registered()
    {
        var result = Catalog().GetDemoRecord();

        var demo = ResultProbe.ValueOf(result, StatusCodes.Status200OK);
        Assert.Equal(AttachableRecordTypes.Demo, demo.RecordType);
        Assert.Equal(DemoRecordId, demo.RecordId);
    }

    [Fact]
    public async Task Without_the_demo_record_type_the_demo_record_is_404()
    {
        var controller = new AttachmentsController(_harness.ListAttachmentTypes, _harness.RemoveAttachment, new AttachableRecords([]));

        var result = controller.GetDemoRecord();

        Assert.Equal((StatusCodes.Status404NotFound, AttachmentsErrorCodes.RecordTypeUnknown), await ResultProbe.RefusalOf(result.Result));
    }

    private AttachmentsController Catalog() => new(_harness.ListAttachmentTypes, _harness.RemoveAttachment, _harness.Records);

    private RecordAttachmentsController Records() =>
        new(_harness.ListRecordAttachments, _harness.IssueUploadTicket, _harness.LinkUploadedFile);

    private AttachmentContentController Content() => new(_harness.DownloadAttachment);
}
