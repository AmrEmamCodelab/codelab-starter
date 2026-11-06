using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Codelab.Attachments.Api.Tests.TestSupport;
using Codelab.Attachments.Core.Errors;
using Codelab.Attachments.Core.Storage;
using Codelab.Errors;
using Codelab.Security.Uploads;

namespace Codelab.Attachments.Api.Tests.Http;

/// <summary>GET and DELETE /api/files/{fileId}: S2S only, one assertion per request, and the stored bytes come back as stored.</summary>
public sealed class FileEndpointsTests : IClassFixture<AttachmentsServiceHostFixture>
{
    private readonly AttachmentsServiceHost _host;
    private readonly HttpClient _client;

    public FileEndpointsTests(AttachmentsServiceHostFixture fixture)
    {
        ArgumentNullException.ThrowIfNull(fixture);
        _host = fixture.Host;
        _client = fixture.Client;
    }

    [Fact]
    public async Task Download_returns_the_stored_bytes_with_the_allowlisted_type_as_an_attachment()
    {
        var fileId = await UploadAsync("scan.pdf", Samples.Pdf);

        using var response = await _client.SendAsync(_host.ServiceRequest(HttpMethod.Get, FilePath(fileId)));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(Samples.Pdf, await response.Content.ReadAsByteArrayAsync());
        Assert.Equal(UploadContentTypes.Pdf, response.Content.Headers.ContentType?.MediaType);
        Assert.Equal("attachment", response.Content.Headers.ContentDisposition?.DispositionType);
        Assert.Equal("scan.pdf", response.Content.Headers.ContentDisposition?.FileNameStar);
        Assert.Equal("nosniff", Assert.Single(response.Headers.GetValues("X-Content-Type-Options")));
        Assert.True(response.Headers.CacheControl?.NoStore);
    }

    [Fact]
    public async Task Delete_removes_the_file_once()
    {
        var fileId = await UploadAsync("old.png", Samples.Png);

        using var deleted = await _client.SendAsync(_host.ServiceRequest(HttpMethod.Delete, FilePath(fileId)));
        using var deletedAgain = await _client.SendAsync(_host.ServiceRequest(HttpMethod.Delete, FilePath(fileId)));
        using var download = await _client.SendAsync(_host.ServiceRequest(HttpMethod.Get, FilePath(fileId)));

        Assert.Equal(HttpStatusCode.NoContent, deleted.StatusCode);
        await Samples.AssertProblemAsync(deletedAgain, HttpStatusCode.NotFound, AttachmentServiceErrorCodes.FileNotFound);
        await Samples.AssertProblemAsync(download, HttpStatusCode.NotFound, AttachmentServiceErrorCodes.FileNotFound);
    }

    [Theory]
    [InlineData("not-an-id")]
    [InlineData("..%2F..%2Fappsettings.json")]
    public async Task A_malformed_id_is_not_found(string fileId)
    {
        using var response = await _client.SendAsync(_host.ServiceRequest(HttpMethod.Get, FilePath(fileId)));

        await Samples.AssertProblemAsync(response, HttpStatusCode.NotFound, AttachmentServiceErrorCodes.FileNotFound);
    }

    [Fact]
    public async Task An_unknown_well_formed_id_is_not_found()
    {
        using var response = await _client.SendAsync(_host.ServiceRequest(HttpMethod.Get, FilePath(AttachmentFileIds.New())));

        await Samples.AssertProblemAsync(response, HttpStatusCode.NotFound, AttachmentServiceErrorCodes.FileNotFound);
    }

    [Fact]
    public async Task A_replayed_service_assertion_is_refused()
    {
        var fileId = await UploadAsync("twice.pdf", Samples.Pdf);
        using var first = _host.ServiceRequest(HttpMethod.Get, FilePath(fileId));
        var assertion = first.Headers.GetValues(_host.Protocol.ServiceAssertionHeaderName).Single();
        using var replay = new HttpRequestMessage(HttpMethod.Get, FilePath(fileId));
        replay.Headers.Add(_host.Protocol.ServiceAssertionHeaderName, assertion);

        using var accepted = await _client.SendAsync(first);
        using var refused = await _client.SendAsync(replay);

        Assert.Equal(HttpStatusCode.OK, accepted.StatusCode);
        await Samples.AssertProblemAsync(refused, HttpStatusCode.Unauthorized, ErrorCodes.Unauthenticated);
    }

    [Fact]
    public async Task An_assertion_for_another_method_is_refused()
    {
        var fileId = await UploadAsync("keep.pdf", Samples.Pdf);
        using var forGet = _host.ServiceRequest(HttpMethod.Get, FilePath(fileId));
        using var delete = new HttpRequestMessage(HttpMethod.Delete, FilePath(fileId));
        delete.Headers.Add(_host.Protocol.ServiceAssertionHeaderName, forGet.Headers.GetValues(_host.Protocol.ServiceAssertionHeaderName).Single());

        using var response = await _client.SendAsync(delete);

        await Samples.AssertProblemAsync(response, HttpStatusCode.Unauthorized);
        Assert.NotEmpty(Directory.GetFiles(_host.StorageRoot, fileId, SearchOption.AllDirectories));
    }

    [Theory]
    [InlineData("GET")]
    [InlineData("DELETE")]
    public async Task An_upload_ticket_does_not_reach_a_file_and_is_not_consumed(string method)
    {
        var fileId = await UploadAsync("private.pdf", Samples.Pdf);
        var ticket = _host.SealTicket();
        using var request = new HttpRequestMessage(new HttpMethod(method), FilePath(fileId));
        request.Headers.Add(_host.TicketHeader, ticket);

        using var refused = await _client.SendAsync(request);
        using var upload = await _client.SendAsync(_host.UploadRequest(ticket, ("later.pdf", Samples.Pdf)));

        await Samples.AssertProblemAsync(refused, HttpStatusCode.Unauthorized, ErrorCodes.Unauthenticated);
        Assert.Equal(HttpStatusCode.OK, upload.StatusCode);
    }

    [Fact]
    public async Task A_bearer_token_is_refused()
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, FilePath(AttachmentFileIds.New()));
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", HttpCredentials.UnsignedJwt);

        using var response = await _client.SendAsync(request);

        await Samples.AssertProblemAsync(response, HttpStatusCode.Unauthorized, ErrorCodes.Unauthenticated);
    }

    [Fact]
    public async Task An_api_key_is_refused()
    {
        using var request = new HttpRequestMessage(HttpMethod.Delete, FilePath(AttachmentFileIds.New()));
        request.Headers.Add(HttpCredentials.ApiKeyHeader, HttpCredentials.ApiKey);

        using var response = await _client.SendAsync(request);

        await Samples.AssertProblemAsync(response, HttpStatusCode.Unauthorized, ErrorCodes.Unauthenticated);
    }

    [Fact]
    public async Task No_credential_is_refused()
    {
        using var response = await _client.GetAsync(FilePath(AttachmentFileIds.New()));

        await Samples.AssertProblemAsync(response, HttpStatusCode.Unauthorized, ErrorCodes.Unauthenticated);
    }

    private static string FilePath(string fileId) => "/api/files/" + fileId;

    private async Task<string> UploadAsync(string fileName, byte[] content)
    {
        using var response = await _client.SendAsync(_host.UploadRequest(_host.SealTicket(), (fileName, content)));
        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadFromJsonAsync<UploadBody>();
        return Assert.Single(body!.Files).FileId;
    }

    private sealed record UploadBody(IReadOnlyList<UploadedFileId> Files);

    private sealed record UploadedFileId(string FileId);
}
