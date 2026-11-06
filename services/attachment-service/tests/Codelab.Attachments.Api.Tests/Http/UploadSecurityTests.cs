using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Codelab.Attachments.Api.Endpoints;
using Codelab.Attachments.Api.Tests.TestSupport;
using Codelab.Attachments.Core.Errors;
using Codelab.Security.Uploads;

namespace Codelab.Attachments.Api.Tests.Http;

/// <summary>POST /api/uploads: the ticket is the only credential, it opens once, and every refusal is a coded problem.</summary>
public sealed class UploadSecurityTests : IClassFixture<AttachmentsServiceHostFixture>
{
    private readonly AttachmentsServiceHost _host;
    private readonly HttpClient _client;

    public UploadSecurityTests(AttachmentsServiceHostFixture fixture)
    {
        ArgumentNullException.ThrowIfNull(fixture);
        _host = fixture.Host;
        _client = fixture.Client;
    }

    [Fact]
    public async Task A_valid_ticket_stores_the_file_and_the_receipt_verifies_for_the_ticket_subject()
    {
        using var response = await _client.SendAsync(_host.UploadRequest(_host.SealTicket(), ("report.pdf", Samples.Pdf)));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<UploadBody>();
        var file = Assert.Single(body!.Files);
        Assert.Equal("report.pdf", file.FileName);
        Assert.Equal(Samples.Pdf.Length, file.SizeBytes);
        Assert.Equal(UploadContentTypes.Pdf, file.ContentType);
        Assert.Equal(Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(Samples.Pdf)), file.Sha256);
        Assert.True(_host.ApiReceiptVerifier().TryVerify(file.Receipt, out var receipt));
        Assert.Equal(AttachmentsServiceHost.Subject, receipt.Scope);
        Assert.Equal(file.FileId, receipt.FileId);
        Assert.Equal(file.Sha256, receipt.Sha256Hex);
    }

    [Fact]
    public async Task A_ticket_opens_once()
    {
        var ticket = _host.SealTicket();
        using var first = await _client.SendAsync(_host.UploadRequest(ticket, ("a.pdf", Samples.Pdf)));
        using var second = await _client.SendAsync(_host.UploadRequest(ticket, ("b.pdf", Samples.Pdf)));

        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        await Samples.AssertProblemAsync(second, HttpStatusCode.Unauthorized, AttachmentServiceErrorCodes.TicketInvalid);
    }

    [Fact]
    public async Task A_tampered_ticket_is_refused()
    {
        var ticket = _host.SealTicket().ToCharArray();
        var middle = ticket.Length / 2;
        ticket[middle] = ticket[middle] == 'A' ? 'B' : 'A';

        using var response = await _client.SendAsync(_host.UploadRequest(new string(ticket), ("a.pdf", Samples.Pdf)));

        await Samples.AssertProblemAsync(response, HttpStatusCode.Unauthorized, AttachmentServiceErrorCodes.TicketInvalid);
    }

    [Fact]
    public async Task A_ticket_sealed_for_another_purpose_is_refused()
    {
        using var response = await _client.SendAsync(_host.UploadRequest(_host.SealTicket(purpose: "account-activation"), ("a.pdf", Samples.Pdf)));

        await Samples.AssertProblemAsync(response, HttpStatusCode.Unauthorized, AttachmentServiceErrorCodes.TicketInvalid);
    }

    [Fact]
    public async Task No_ticket_is_refused()
    {
        using var response = await _client.SendAsync(_host.UploadRequest(ticket: null, ("a.pdf", Samples.Pdf)));

        await Samples.AssertProblemAsync(response, HttpStatusCode.Unauthorized, AttachmentServiceErrorCodes.TicketInvalid);
    }

    [Fact]
    public async Task A_bearer_token_is_not_a_credential()
    {
        using var request = _host.UploadRequest(ticket: null, ("a.pdf", Samples.Pdf));
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", HttpCredentials.UnsignedJwt);

        using var response = await _client.SendAsync(request);

        await Samples.AssertProblemAsync(response, HttpStatusCode.Unauthorized, AttachmentServiceErrorCodes.TicketInvalid);
    }

    [Fact]
    public async Task An_api_key_is_not_a_credential()
    {
        using var request = _host.UploadRequest(ticket: null, ("a.pdf", Samples.Pdf));
        request.Headers.Add(HttpCredentials.ApiKeyHeader, HttpCredentials.ApiKey);

        using var response = await _client.SendAsync(request);

        await Samples.AssertProblemAsync(response, HttpStatusCode.Unauthorized, AttachmentServiceErrorCodes.TicketInvalid);
    }

    [Fact]
    public async Task A_polyglot_file_is_refused_by_its_magic_bytes()
    {
        using var response = await _client.SendAsync(_host.UploadRequest(_host.SealTicket(), ("image.pdf", Samples.Png)));

        await Samples.AssertProblemAsync(response, HttpStatusCode.UnsupportedMediaType, AttachmentServiceErrorCodes.SignatureMismatch);
    }

    [Fact]
    public async Task A_traversal_name_is_reduced_to_its_leaf_and_stored_under_the_root()
    {
        using var response = await _client.SendAsync(_host.UploadRequest(_host.SealTicket(), ("../../escape.pdf", Samples.Pdf)));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var file = Assert.Single((await response.Content.ReadFromJsonAsync<UploadBody>())!.Files);
        Assert.Equal("escape.pdf", file.FileName);
        var parent = Directory.GetParent(_host.StorageRoot)!.FullName;
        Assert.Empty(Directory.GetFiles(parent, "escape.pdf", SearchOption.TopDirectoryOnly));
        Assert.Single(Directory.GetFiles(_host.StorageRoot, file.FileId, SearchOption.AllDirectories));
    }

    [Fact]
    public async Task A_file_over_the_cap_is_refused_with_a_reference_code()
    {
        var oversized = Samples.PdfOfSize(AttachmentsServiceHost.MaxFileBytes + 1);

        using var response = await _client.SendAsync(_host.UploadRequest(_host.SealTicket(), ("big.pdf", oversized)));

        await Samples.AssertProblemAsync(response, HttpStatusCode.RequestEntityTooLarge, AttachmentServiceErrorCodes.FileTooLarge);
    }

    [Fact]
    public async Task A_declared_body_above_the_ceiling_is_refused_before_it_is_read()
    {
        using var request = _host.UploadRequest(_host.SealTicket(), ("a.pdf", Samples.Pdf));
        var ceiling = UploadBodyCeiling.For(new UploadSafetyOptions { MaxTotalBytes = AttachmentsServiceHost.MaxTotalBytes, MaxFileCount = AttachmentsServiceHost.MaxFileCount });
        request.Content = new ByteArrayContent(new byte[ceiling + 1]);
        request.Content.Headers.ContentType = MediaTypeHeaderValue.Parse("multipart/form-data; boundary=x");

        using var response = await _client.SendAsync(request);

        await Samples.AssertProblemAsync(response, HttpStatusCode.RequestEntityTooLarge, AttachmentServiceErrorCodes.FileTooLarge);
    }

    [Fact]
    public async Task More_files_than_the_cap_are_refused()
    {
        var files = Enumerable.Range(0, AttachmentsServiceHost.MaxFileCount + 1).Select(index => ($"f{index}.pdf", Samples.Pdf)).ToArray();

        using var response = await _client.SendAsync(_host.UploadRequest(_host.SealTicket(), files));

        await Samples.AssertProblemAsync(response, HttpStatusCode.RequestEntityTooLarge, AttachmentServiceErrorCodes.TooManyFiles);
    }

    [Fact]
    public async Task A_body_that_is_not_multipart_is_refused()
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, AttachmentsRoutes.Uploads) { Content = JsonContent.Create(new { file = "x" }) };
        request.Headers.Add(_host.TicketHeader, _host.SealTicket());

        using var response = await _client.SendAsync(request);

        await Samples.AssertProblemAsync(response, HttpStatusCode.UnsupportedMediaType, AttachmentServiceErrorCodes.TypeNotAllowed);
    }

    private sealed record UploadBody(IReadOnlyList<UploadedFileBody> Files);

    private sealed record UploadedFileBody(string FileId, string FileName, long SizeBytes, string ContentType, string Sha256, string Receipt);
}
