using Codelab.Attachments.Core.Errors;
using Codelab.Attachments.Core.Tests.TestSupport;
using Codelab.Attachments.Core.Uploads;
using Codelab.Security.Uploads;
using Microsoft.AspNetCore.Http;

namespace Codelab.Attachments.Core.Tests.Uploads;

public sealed class UploadPipelineTests
{
    [Fact]
    public async Task A_valid_pdf_is_stored_with_its_digest_content_type_and_a_receipt_for_the_scope()
    {
        using var fixture = new UploadFixture();
        var content = TestFiles.Pdf();

        var outcome = await fixture.RunAsync(new MultipartBody().File("report.pdf", content));

        Assert.False(outcome.IsRefused);
        var file = Assert.Single(outcome.Files);
        Assert.Equal("report.pdf", file.FileName);
        Assert.Equal(content.Length, file.SizeBytes);
        Assert.Equal(UploadContentTypes.Pdf, file.ContentType);
        Assert.Equal(TestFiles.Sha256Hex(content), file.Sha256);
        Assert.True(fixture.Signer.TryVerify(file.Receipt, out var receipt));
        Assert.Equal(new UploadReceipt(UploadFixture.Scope, file.FileId, content.Length, file.Sha256, "report.pdf", fixture.Clock.GetUtcNow().ToUnixTimeSeconds()), receipt);
    }

    [Fact]
    public async Task Several_files_are_stored_in_request_order()
    {
        using var fixture = new UploadFixture();

        var outcome = await fixture.RunAsync(new MultipartBody().File("a.pdf", TestFiles.Pdf()).File("b.png", TestFiles.Png()).File("c.jpeg", TestFiles.Jpeg()));

        Assert.Equal(["a.pdf", "b.png", "c.jpeg"], outcome.Files.Select(file => file.FileName));
        Assert.Equal([UploadContentTypes.Pdf, UploadContentTypes.Png, UploadContentTypes.Jpeg], outcome.Files.Select(file => file.ContentType));
        Assert.Equal(outcome.Files.Count, outcome.Files.Select(file => file.FileId).Distinct().Count());
    }

    [Theory]
    [MemberData(nameof(PolyglotContents))]
    public async Task A_file_whose_bytes_do_not_match_its_extension_is_refused(string fileName, byte[] content)
    {
        using var fixture = new UploadFixture();

        var outcome = await fixture.RunAsync(new MultipartBody().File(fileName, content));

        AssertRefused(outcome, UploadRefusalReason.SignatureMismatch, AttachmentServiceErrorCodes.SignatureMismatch, StatusCodes.Status415UnsupportedMediaType);
        Assert.Empty(fixture.Root.AllFiles());
    }

    public static TheoryData<string, byte[]> PolyglotContents() => new()
    {
        { "scan.pdf", TestFiles.Png() },
        { "scan.pdf", TestFiles.Exe() },
        { "photo.png", TestFiles.Pdf() },
        { "photo.jpg", TestFiles.Exe() },
    };

    [Theory]
    [InlineData(@"..\..\x.pdf")]
    [InlineData("../../x.pdf")]
    [InlineData(@"C:\Windows\System32\x.pdf")]
    [InlineData("/etc/x.pdf")]
    [InlineData(@"\\files.example\share\x.pdf")]
    [InlineData("x.pdf. . ")]
    public async Task A_path_in_the_client_name_is_reduced_to_its_leaf_and_the_file_stays_under_the_root(string clientName)
    {
        using var fixture = new UploadFixture();

        var outcome = await fixture.RunAsync(new MultipartBody().File(clientName, TestFiles.Pdf()));

        var file = Assert.Single(outcome.Files);
        Assert.Equal("x.pdf", file.FileName);
        Assert.All(fixture.Root.AllFiles(), path => Assert.StartsWith(fixture.Root.Path, path, StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(fixture.Root.AllFiles(), path => path.EndsWith("x.pdf", StringComparison.OrdinalIgnoreCase));
    }

    [Theory]
    [InlineData("a.pdf:evil")]
    [InlineData("a.pdf:evil.exe")]
    [InlineData("tool.exe")]
    [InlineData("TOOL.EXE. ")]
    [InlineData("script.ps1")]
    [InlineData("image.gif")]
    [InlineData("noextension")]
    [InlineData("..")]
    public async Task A_name_that_is_blocked_unusable_or_not_allowed_is_refused_before_any_byte_is_written(string clientName)
    {
        using var fixture = new UploadFixture();

        var outcome = await fixture.RunAsync(new MultipartBody().File(clientName, TestFiles.Pdf()));

        AssertRefused(outcome, UploadRefusalReason.TypeNotAllowed, AttachmentServiceErrorCodes.TypeNotAllowed, StatusCodes.Status415UnsupportedMediaType);
        Assert.Empty(fixture.Root.AllFiles());
    }

    [Fact]
    public async Task A_blocked_extension_is_refused_even_when_the_allowlist_names_it()
    {
        var options = new UploadSafetyOptions { AllowedExtensions = [".pdf", ".zip"], BlockedExtensions = [".zip"] };
        using var fixture = new UploadFixture(options);

        var outcome = await fixture.RunAsync(new MultipartBody().File("bundle.zip", [0x50, 0x4B, 0x03, 0x04, 0x00]));

        AssertRefused(outcome, UploadRefusalReason.TypeNotAllowed, AttachmentServiceErrorCodes.TypeNotAllowed, StatusCodes.Status415UnsupportedMediaType);
    }

    [Fact]
    public async Task A_file_over_the_per_file_cap_is_refused_and_nothing_is_kept()
    {
        using var fixture = new UploadFixture(new UploadSafetyOptions { MaxFileBytes = 100 });

        var outcome = await fixture.RunAsync(new MultipartBody().File("big.pdf", TestFiles.Pdf(fillerBytes: 200)));

        AssertRefused(outcome, UploadRefusalReason.FileTooLarge, AttachmentServiceErrorCodes.FileTooLarge, StatusCodes.Status413PayloadTooLarge);
        Assert.Empty(fixture.Root.AllFiles());
    }

    [Fact]
    public async Task A_file_exactly_at_the_per_file_cap_is_accepted()
    {
        var content = TestFiles.Pdf(fillerBytes: 95);
        using var fixture = new UploadFixture(new UploadSafetyOptions { MaxFileBytes = content.Length });

        var outcome = await fixture.RunAsync(new MultipartBody().File("edge.pdf", content));

        Assert.False(outcome.IsRefused);
    }

    [Fact]
    public async Task One_file_too_many_is_refused_and_the_files_already_stored_are_removed()
    {
        using var fixture = new UploadFixture(new UploadSafetyOptions { MaxFileCount = 2 });

        var outcome = await fixture.RunAsync(new MultipartBody().File("a.pdf", TestFiles.Pdf()).File("b.pdf", TestFiles.Pdf()).File("c.pdf", TestFiles.Pdf()));

        AssertRefused(outcome, UploadRefusalReason.TooManyFiles, AttachmentServiceErrorCodes.TooManyFiles, StatusCodes.Status413PayloadTooLarge);
        Assert.Empty(fixture.Root.AllFiles());
    }

    [Fact]
    public async Task A_request_over_the_total_cap_is_refused_as_too_large_and_nothing_is_kept()
    {
        using var fixture = new UploadFixture(new UploadSafetyOptions { MaxFileBytes = 1000, MaxTotalBytes = 150 });

        var outcome = await fixture.RunAsync(new MultipartBody().File("a.pdf", TestFiles.Pdf(fillerBytes: 95)).File("b.pdf", TestFiles.Pdf(fillerBytes: 95)));

        AssertRefused(outcome, UploadRefusalReason.TotalTooLarge, AttachmentServiceErrorCodes.FileTooLarge, StatusCodes.Status413PayloadTooLarge);
        Assert.Empty(fixture.Root.AllFiles());
    }

    [Fact]
    public async Task A_later_refused_file_rolls_back_only_this_requests_files()
    {
        using var fixture = new UploadFixture();
        var earlier = await fixture.RunAsync(new MultipartBody().File("kept.pdf", TestFiles.Pdf()));
        var filesBefore = fixture.Root.AllFiles();

        var outcome = await fixture.RunAsync(new MultipartBody().File("a.pdf", TestFiles.Pdf()).File("b.pdf", TestFiles.Exe()));

        Assert.True(outcome.IsRefused);
        Assert.Equal(filesBefore, fixture.Root.AllFiles());
        await using var kept = await fixture.Store.OpenReadAsync(Assert.Single(earlier.Files).FileId, CancellationToken.None);
        Assert.NotNull(kept);
    }

    [Fact]
    public async Task An_empty_file_is_refused()
    {
        using var fixture = new UploadFixture();

        var outcome = await fixture.RunAsync(new MultipartBody().File("empty.pdf", []));

        AssertRefused(outcome, UploadRefusalReason.EmptyFile, AttachmentServiceErrorCodes.EmptyFile, StatusCodes.Status400BadRequest);
        Assert.Empty(fixture.Root.AllFiles());
    }

    [Fact]
    public async Task A_body_without_file_parts_is_refused()
    {
        using var fixture = new UploadFixture();

        var outcome = await fixture.RunAsync(new MultipartBody());

        AssertRefused(outcome, UploadRefusalReason.NoFile, AttachmentServiceErrorCodes.EmptyFile, StatusCodes.Status400BadRequest);
    }

    [Fact]
    public async Task A_part_that_is_not_a_file_named_file_is_refused_and_earlier_files_are_removed()
    {
        using var fixture = new UploadFixture();

        var fieldOutcome = await fixture.RunAsync(new MultipartBody().File("a.pdf", TestFiles.Pdf()).Field("note", "hello"));
        var renamedOutcome = await fixture.RunAsync(new MultipartBody().File("a.pdf", TestFiles.Pdf(), partName: "files"));

        AssertRefused(fieldOutcome, UploadRefusalReason.UnexpectedPart, AttachmentServiceErrorCodes.TypeNotAllowed, StatusCodes.Status415UnsupportedMediaType);
        AssertRefused(renamedOutcome, UploadRefusalReason.UnexpectedPart, AttachmentServiceErrorCodes.TypeNotAllowed, StatusCodes.Status415UnsupportedMediaType);
        Assert.Empty(fixture.Root.AllFiles());
    }

    [Theory]
    [InlineData(null)]
    [InlineData("application/json")]
    [InlineData("multipart/form-data")]
    [InlineData("multipart/mixed; boundary=abc")]
    public async Task A_request_that_is_not_multipart_form_data_with_a_boundary_is_refused(string? contentType)
    {
        using var fixture = new UploadFixture();

        var outcome = await fixture.Pipeline.RunAsync(new UploadRequest(new MemoryStream(TestFiles.Pdf()), contentType, UploadFixture.Scope), CancellationToken.None);

        AssertRefused(outcome, UploadRefusalReason.NotMultipart, AttachmentServiceErrorCodes.TypeNotAllowed, StatusCodes.Status415UnsupportedMediaType);
    }

    [Fact]
    public async Task A_malformed_multipart_body_is_refused_as_a_client_error()
    {
        using var fixture = new UploadFixture();
        var headerFlood = "--" + MultipartBody.ContentType.Split("boundary=")[1] + "\r\nX-Flood: " + new string('a', 20000) + "\r\n\r\n";

        var outcome = await fixture.Pipeline.RunAsync(new UploadRequest(new MemoryStream(System.Text.Encoding.ASCII.GetBytes(headerFlood)), MultipartBody.ContentType, UploadFixture.Scope), CancellationToken.None);

        AssertRefused(outcome, UploadRefusalReason.MalformedBody, AttachmentServiceErrorCodes.EmptyFile, StatusCodes.Status400BadRequest);
    }

    [Fact]
    public async Task A_cancelled_request_removes_the_files_it_stored()
    {
        using var fixture = new UploadFixture();
        using var cancellation = new CancellationTokenSource();
        var body = new MultipartBody().File("a.pdf", TestFiles.Pdf()).File("b.pdf", TestFiles.Pdf()).Build();
        var stream = new CancelAfterBytesStream(body, cancelAfterBytes: body.Length - 10, cancellation);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => fixture.Pipeline.RunAsync(new UploadRequest(stream, MultipartBody.ContentType, UploadFixture.Scope), cancellation.Token));

        Assert.Empty(fixture.Root.AllFiles());
    }

    [Fact]
    public async Task A_tampered_receipt_fails_verification()
    {
        using var fixture = new UploadFixture();
        var outcome = await fixture.RunAsync(new MultipartBody().File("report.pdf", TestFiles.Pdf()));
        var token = Assert.Single(outcome.Files).Receipt;
        var parts = token.Split('.');
        var forgedPayload = Base64UrlJson.Rewrite(parts[2], json => json.Replace("attachable-records~2a", "attachable-records~2b", StringComparison.Ordinal));

        Assert.False(fixture.Signer.TryVerify(string.Join('.', parts[0], parts[1], forgedPayload, parts[3]), out _));
        Assert.False(fixture.Signer.TryVerify(token[..^2] + (token[^2] == 'A' ? "BB" : "AA"), out _));
        Assert.False(new UploadReceiptSigner(UploadFixture.NewReceiptRing(), Codelab.Security.SecurityProtocol.Create(Codelab.Security.SecurityProtocol.TestPrefix)).TryVerify(token, out _));
    }

    [Fact]
    public async Task The_pipeline_refuses_a_missing_scope()
    {
        using var fixture = new UploadFixture();

        await Assert.ThrowsAsync<ArgumentException>(() => fixture.Pipeline.RunAsync(new MultipartBody().File("a.pdf", TestFiles.Pdf()).ToRequest(" "), CancellationToken.None));
    }

    private static void AssertRefused(UploadOutcome outcome, UploadRefusalReason reason, string code, int status)
    {
        Assert.True(outcome.IsRefused);
        Assert.Empty(outcome.Files);
        Assert.Equal(reason, outcome.Refusal!.Reason);
        Assert.Equal(code, outcome.Refusal.Code);
        Assert.Equal(status, outcome.Refusal.StatusCode);
    }

    /// <summary>A body that cancels the request once a number of bytes were read (a client that goes away mid-upload).</summary>
    private sealed class CancelAfterBytesStream(byte[] content, int cancelAfterBytes, CancellationTokenSource cancellation) : MemoryStream(content)
    {
        public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) =>
            ReadAsync(buffer.AsMemory(offset, count), cancellationToken).AsTask();

        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            if (Position >= cancelAfterBytes)
            {
                await cancellation.CancelAsync();
                cancellationToken.ThrowIfCancellationRequested();
            }

            return await base.ReadAsync(buffer[..Math.Min(buffer.Length, 16)], cancellationToken);
        }
    }

    private static class Base64UrlJson
    {
        public static string Rewrite(string payload, Func<string, string> change)
        {
            var json = System.Text.Encoding.UTF8.GetString(System.Buffers.Text.Base64Url.DecodeFromChars(payload));
            return System.Buffers.Text.Base64Url.EncodeToString(System.Text.Encoding.UTF8.GetBytes(change(json)));
        }
    }
}
