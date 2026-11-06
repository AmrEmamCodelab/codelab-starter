using Starter.Application.Results;
using Starter.Application.Validation;
using Starter.Domain.Attachments;
using Starter.Domain.Exceptions;
using Starter.Modules.Attachments.Application;
using Starter.Modules.Attachments.Domain;
using Starter.Modules.Attachments.Tests.Fakes;

namespace Starter.Modules.Attachments.Tests.Application;

public sealed class LinkUploadedFileTests
{
    private readonly Harness _harness = new();

    [Fact]
    public async Task A_verified_receipt_is_linked_with_the_receipt_metadata_only()
    {
        var receipt = _harness.Receipt();

        var outcome = await Link(receipt);

        Assert.True(outcome.IsSuccess);
        var stored = Assert.Single(_harness.Store.Attachments);
        Assert.Equal((stored.Id, AttachmentTypeKeys.DemoDocument, "report.pdf", 1024L, "application/pdf", Harness.Now), (outcome.Value.Id, outcome.Value.TypeKey, outcome.Value.FileName, outcome.Value.SizeBytes, outcome.Value.ContentType, outcome.Value.UploadedAt));
        Assert.Equal((TestData.Sha256, TestData.User, AttachmentTypeIds.DemoDocument), (stored.Sha256Hex, stored.UploadedBySubject, stored.ServiceAttachmentId));
        Assert.Equal(1, _harness.Store.Saves);
    }

    [Fact]
    public async Task A_tampered_receipt_is_refused()
    {
        var outcome = await Link("r1.forged.payload.mac");

        Harness.AssertRefused(outcome, RefusalCategory.Invalid, AttachmentsErrorCodes.ReceiptInvalid);
        Assert.Empty(_harness.Store.Attachments);
    }

    [Fact]
    public async Task A_receipt_issued_to_another_user_is_refused()
    {
        var outcome = await Link(_harness.Receipt(scopeUser: TestData.OtherUser));

        Harness.AssertRefused(outcome, RefusalCategory.Invalid, AttachmentsErrorCodes.ReceiptInvalid);
        Assert.Empty(_harness.Store.Attachments);
    }

    [Fact]
    public async Task A_receipt_issued_for_another_record_is_refused()
    {
        var outcome = await Link(_harness.Receipt(recordId: TestData.RecordId + 1));

        Harness.AssertRefused(outcome, RefusalCategory.Invalid, AttachmentsErrorCodes.ReceiptInvalid);
    }

    [Fact]
    public async Task A_receipt_issued_for_another_type_is_refused()
    {
        var receipt = _harness.Receipt(fileName: "photo.jpg");

        var outcome = await _harness.LinkUploadedFile.ExecuteAsync(TestData.Demo, new LinkUploadedFileCommand(TestData.OptionalTypeKey, receipt), CancellationToken.None);

        Harness.AssertRefused(outcome, RefusalCategory.Invalid, AttachmentsErrorCodes.ReceiptInvalid);
    }

    [Fact]
    public async Task A_verified_receipt_with_a_malformed_file_is_refused()
    {
        var receipt = _harness.Receipt();
        _harness.Verifier.Receipts[receipt] = _harness.Verifier.Receipts[receipt] with { File = TestData.File(fileName: "../escape.pdf") };

        var outcome = await Link(receipt);

        Harness.AssertRefused(outcome, RefusalCategory.Invalid, AttachmentsErrorCodes.ReceiptInvalid);
    }

    [Fact]
    public async Task Linking_the_same_receipt_twice_answers_the_existing_attachment()
    {
        var receipt = _harness.Receipt();
        var first = await Link(receipt);

        var second = await Link(receipt);

        Assert.True(second.IsSuccess);
        Assert.Equal(first.Value.Id, second.Value.Id);
        Assert.Single(_harness.Store.Attachments);
        Assert.Equal(1, _harness.Store.Saves);
    }

    [Fact]
    public async Task A_retry_succeeds_even_when_the_type_is_full_now()
    {
        var receipt = _harness.Receipt();
        await Link(receipt);
        _harness.StoredDemo("fileIdBravo00000000002");

        var retry = await Link(receipt);

        Assert.True(retry.IsSuccess);
    }

    [Fact]
    public async Task A_file_already_linked_elsewhere_is_refused()
    {
        _harness.Store.Stored(TestData.Link(_harness.DemoType, "fileIdAlpha00000000001", recordId: 42));

        var outcome = await Link(_harness.Receipt());

        Harness.AssertRefused(outcome, RefusalCategory.Invalid, AttachmentsErrorCodes.ReceiptInvalid);
    }

    [Fact]
    public async Task A_removed_file_cannot_be_linked_again()
    {
        var receipt = _harness.Receipt();
        var linked = await Link(receipt);
        await _harness.RemoveAttachment.ExecuteAsync(linked.Value.Id, CancellationToken.None);

        var outcome = await Link(receipt);

        Harness.AssertRefused(outcome, RefusalCategory.Invalid, AttachmentsErrorCodes.ReceiptInvalid);
    }

    [Fact]
    public async Task An_extension_the_type_does_not_allow_is_refused()
    {
        var outcome = await Link(_harness.Receipt(fileName: "photo.jpg"));

        Harness.AssertRefused(outcome, RefusalCategory.Invalid, AttachmentsErrorCodes.ExtensionNotAllowedForType);
        Assert.Empty(_harness.Store.Attachments);
    }

    [Fact]
    public async Task A_full_type_refuses_another_file()
    {
        _harness.StoredDemo("fileIdBravo00000000002");
        _harness.StoredDemo("fileIdCharlie000000003");

        var outcome = await Link(_harness.Receipt());

        Harness.AssertRefused(outcome, RefusalCategory.Conflict, AttachmentsErrorCodes.MaxFilesReached);
        Assert.Equal(0, _harness.Store.Saves);
    }

    [Fact]
    public async Task A_record_the_user_may_not_change_answers_not_found_before_the_receipt_is_read()
    {
        _harness.Access.CanChange = false;

        var outcome = await Link(_harness.Receipt());

        Harness.AssertRefused(outcome, RefusalCategory.NotFound, AttachmentsErrorCodes.NotFound);
        Assert.Equal([(RecordAccessKind.Change, TestData.RecordId)], _harness.Access.Asked);
    }

    [Fact]
    public async Task An_unknown_record_type_is_refused()
    {
        var outcome = await _harness.LinkUploadedFile.ExecuteAsync(
            new AttachableRecord("unknown-records", TestData.RecordId), new LinkUploadedFileCommand(AttachmentTypeKeys.DemoDocument, "r1.x"), CancellationToken.None);

        Harness.AssertRefused(outcome, RefusalCategory.NotFound, AttachmentsErrorCodes.RecordTypeUnknown);
    }

    [Fact]
    public async Task A_retired_type_is_not_found()
    {
        _harness.DemoType.SetActive(false);

        var outcome = await Link(_harness.Receipt());

        Harness.AssertRefused(outcome, RefusalCategory.NotFound, AttachmentsErrorCodes.TypeNotFound);
    }

    [Fact]
    public async Task An_invalid_body_returns_every_field_error()
    {
        var outcome = await _harness.LinkUploadedFile.ExecuteAsync(TestData.Demo, new LinkUploadedFileCommand("Bad Key", null), CancellationToken.None);

        Harness.AssertRefused(outcome, RefusalCategory.Invalid, PlatformErrorCodes.Validation);
        Assert.Equal([ValidationMessages.InvalidFormat], outcome.Refusal!.FieldErrors["typeKey"]);
        Assert.Equal([ValidationMessages.Required], outcome.Refusal.FieldErrors["receipt"]);
    }

    [Fact]
    public async Task An_anonymous_caller_is_denied()
    {
        var anonymous = new Harness(anonymous: true);
        var receipt = anonymous.Receipt();

        await Assert.ThrowsAsync<AccessDeniedException>(() =>
            anonymous.LinkUploadedFile.ExecuteAsync(TestData.Demo, new LinkUploadedFileCommand(AttachmentTypeKeys.DemoDocument, receipt), CancellationToken.None));
    }

    private Task<Outcome<RecordAttachmentView>> Link(string receipt) =>
        _harness.LinkUploadedFile.ExecuteAsync(TestData.Demo, new LinkUploadedFileCommand(AttachmentTypeKeys.DemoDocument, receipt), CancellationToken.None);
}

public sealed class LinkUploadedFileCommandValidatorTests
{
    [Fact]
    public void A_receipt_longer_than_the_limit_is_too_long()
    {
        var errors = new LinkUploadedFileCommandValidator().Validate(
            new LinkUploadedFileCommand(AttachmentTypeKeys.DemoDocument, new string('a', AttachmentsLimits.ReceiptMaxLength + 1)));

        Assert.Equal([ValidationMessages.TooLong], errors.ToDictionary()["receipt"]);
    }

    [Fact]
    public void A_well_formed_body_is_valid()
    {
        Assert.True(new LinkUploadedFileCommandValidator().Validate(new LinkUploadedFileCommand(AttachmentTypeKeys.DemoDocument, "r1.a.b.c")).IsValid);
    }
}
