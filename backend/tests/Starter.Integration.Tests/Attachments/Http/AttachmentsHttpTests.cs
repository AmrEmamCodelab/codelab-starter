using System.Buffers.Text;
using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Codelab.Security;
using Codelab.Security.Problems;
using Codelab.Security.PublicIds;
using Codelab.Security.Tickets;
using Codelab.Security.Uploads;
using Microsoft.Net.Http.Headers;
using Starter.Application.Attachments;
using Starter.Application.Results;
using Starter.Domain.Attachments;
using Starter.Integration.Tests.Support;
using Starter.Modules.Attachments.Api;
using Starter.Modules.Attachments.Application;
using Starter.Modules.Attachments.Infrastructure.Security;
using Starter.Tests.Hosting;

namespace Starter.Integration.Tests.Attachments.Http;

/// <summary>
/// The attachments API on the real host and real SQL (phase 6 exit: a tampered receipt is refused by the API). The
/// test plays the attachment service: it opens the ticket the API issued (purpose <c>attachment-upload</c>) and signs
/// a receipt for its subject with the host's receipt ring, exactly as the service would. Every test works on the
/// Development demo record and asserts on its own file ids only (the collection shares one database).
/// </summary>
[Collection(AttachmentsHttpHostGroup.Name)]
public sealed class AttachmentsHttpTests(AttachmentsHttpHost host)
{
    private const string TypesPath = "/api/attachments/types?recordType=" + AttachableRecordTypes.Demo;
    private const string DemoRecordPath = "/api/attachments/demo-record";
    private const string AttachmentsPath = "/api/attachments/";
    private const string ContentSuffix = "/content";
    private const string UploadPath = "/api/uploads";
    private const string FileName = "report.pdf";
    private const string ScopeSeparator = "|";
    private const char ReceiptSeparator = '.';
    private const int ReceiptPayloadPart = 2;
    private const int RecordIdScopePart = 1;
    private const long OtherRecordId = DemoRecord.RecordId + 1;
    private const int FileIdBytes = 16;
    private const string BearerScheme = "Bearer";
    private const char HeaderListSeparator = ',';

    [Fact]
    public async Task A_ticket_is_issued_for_the_demo_record()
    {
        using var api = host.CreateApi();
        var token = await host.AdminTokenAsync();
        var record = await DemoRecordPathAsync(api, token);

        var ticket = await IssueTicketAsync(api, record, token);

        Assert.False(string.IsNullOrEmpty(ticket.GetProperty("ticket").GetString()));
        Assert.EndsWith("-Upload-Ticket", ticket.GetProperty("ticketHeader").GetString(), StringComparison.Ordinal);
        Assert.Equal(AttachmentsHttpHost.ServiceBaseUrl + UploadPath, ticket.GetProperty("uploadUrl").GetString());
        Assert.True(ticket.GetProperty("maxFileCount").GetInt32() >= 1);
        Assert.Contains(".pdf", ticket.GetProperty("allowedExtensions").EnumerateArray().Select(extension => extension.GetString()));
    }

    [Fact]
    public async Task A_valid_receipt_links_the_file_with_201_and_it_is_listed()
    {
        using var api = host.CreateApi();
        var token = await host.AdminTokenAsync();
        var record = await DemoRecordPathAsync(api, token);
        var upload = await UploadAsync(api, record, token);

        using var linked = await LinkAsync(api, record, upload.Receipt, token);
        var item = await ApiClient.ReadAsync(linked, HttpStatusCode.Created);
        using var list = await api.GetAsync(record, token);
        var body = await ApiClient.ReadAsync(list, HttpStatusCode.OK);

        Assert.Equal(AttachmentTypeKeys.DemoDocument, item.GetProperty("typeKey").GetString());
        Assert.Equal(FileName, item.GetProperty("fileName").GetString());
        Assert.Equal(UploadContentTypes.Pdf, item.GetProperty("contentType").GetString());
        Assert.Contains(body.GetProperty("items").EnumerateArray(), listed => listed.GetProperty("id").GetString() == item.GetProperty("id").GetString());
        Assert.DoesNotContain(AttachmentTypeKeys.DemoDocument, body.GetProperty("missingRequiredTypeKeys").EnumerateArray().Select(key => key.GetString()));
    }

    [Fact]
    public async Task A_tampered_receipt_is_400_receipt_invalid_with_a_reference_code()
    {
        using var api = host.CreateApi();
        var token = await host.AdminTokenAsync();
        var record = await DemoRecordPathAsync(api, token);
        var upload = await UploadAsync(api, record, token);

        using var refused = await LinkAsync(api, record, Tampered(upload.Receipt), token);

        Assert.Equal(AttachmentsErrorCodes.ReceiptInvalid, await StarterProblems.AssertProblemAsync(refused, HttpStatusCode.BadRequest));
    }

    [Fact]
    public async Task The_issued_scope_carries_the_record_public_id_never_its_internal_number()
    {
        using var api = host.CreateApi();
        var token = await host.AdminTokenAsync();
        var record = await DemoRecordPathAsync(api, token);
        var ticket = (await IssueTicketAsync(api, record, token)).GetProperty("ticket").GetString()!;
        var opened = await host.Service<UploadTicketRing>().OpenAsync(UploadTickets.Purpose, ticket, CancellationToken.None);

        var parts = opened.Ticket!.Subject.Split(ScopeSeparator);

        Assert.DoesNotContain(DemoRecord.RecordId.ToString(CultureInfo.InvariantCulture), parts);
        Assert.Equal(host.Service<IPublicIdCodec>().Protect(AttachableRecordIds.Domain, DemoRecord.RecordId), parts[RecordIdScopePart]);
    }

    [Fact]
    public async Task A_receipt_issued_for_another_record_is_refused()
    {
        using var api = host.CreateApi();
        var token = await host.AdminTokenAsync();
        var record = await DemoRecordPathAsync(api, token);
        var upload = await UploadAsync(api, record, token, scope => OtherRecordScope(scope));

        using var refused = await LinkAsync(api, record, upload.Receipt, token);

        Assert.Equal(AttachmentsErrorCodes.ReceiptInvalid, await StarterProblems.AssertProblemAsync(refused, HttpStatusCode.BadRequest));
    }

    [Fact]
    public async Task Without_the_permission_the_endpoints_answer_403_problem_json()
    {
        using var api = host.CreateApi();
        var adminToken = await host.AdminTokenAsync();
        var record = await DemoRecordPathAsync(api, adminToken);
        var userToken = await host.DevUserTokenAsync();

        using var types = await api.GetAsync(TypesPath, userToken);
        using var list = await api.GetAsync(record, userToken);
        using var ticket = await api.PostAsync(record + "/upload-ticket", new { typeKey = AttachmentTypeKeys.DemoDocument }, userToken);

        await StarterProblems.AssertProblemAsync(types, HttpStatusCode.Forbidden);
        await StarterProblems.AssertProblemAsync(list, HttpStatusCode.Forbidden);
        await StarterProblems.AssertProblemAsync(ticket, HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task The_download_streams_the_bytes_without_a_content_disposition()
    {
        using var api = host.CreateApi();
        var token = await host.AdminTokenAsync();
        var record = await DemoRecordPathAsync(api, token);
        var upload = await UploadAsync(api, record, token);
        var id = await LinkedIdAsync(api, record, upload.Receipt, token);

        using var download = await api.GetAsync(AttachmentsPath + id + ContentSuffix, token);

        Assert.Equal(HttpStatusCode.OK, download.StatusCode);
        Assert.Equal(upload.Bytes, await download.Content.ReadAsByteArrayAsync());
        Assert.Equal(UploadContentTypes.Pdf, download.Content.Headers.ContentType?.MediaType);
        Assert.Null(download.Content.Headers.ContentDisposition);
        Assert.Equal(AttachmentFileResult.NoSniff, string.Join(",", download.Headers.GetValues(HeaderNames.XContentTypeOptions).Distinct(StringComparer.Ordinal)));
        Assert.Equal([AttachmentFileResult.SandboxPolicy], download.Headers.GetValues(HeaderNames.ContentSecurityPolicy));
    }

    [Fact]
    public async Task A_cross_origin_download_carries_the_cors_headers_and_the_whole_body()
    {
        using var api = host.CreateApi();
        var token = await host.AdminTokenAsync();
        var record = await DemoRecordPathAsync(api, token);
        var upload = await UploadAsync(api, record, token);
        var id = await LinkedIdAsync(api, record, upload.Receipt, token);

        await using var twin = BrowserTwinHost.Start(host);
        using var browser = twin.CreateBrowser();
        using var download = await browser.GetAsync(AttachmentsPath + id + ContentSuffix, await twin.AdminTokenAsync(), BrowserTwinHost.WebOrigin);

        Assert.Equal(HttpStatusCode.OK, download.StatusCode);
        Assert.Equal([BrowserTwinHost.WebOrigin], download.Headers.GetValues(HeaderNames.AccessControlAllowOrigin));
        Assert.True(download.Headers.Contains(host.Service<SecurityProtocol>().ResponseSignatureHeaderName));
        Assert.Equal(upload.Bytes, await download.Content.ReadAsByteArrayAsync());
        Assert.Contains(host.Service<SecurityProtocol>().ResponseSignatureHeaderName, ExposedHeaders(download), StringComparer.OrdinalIgnoreCase);
        // A Content-Disposition makes browser download managers swallow the fetch (the phase 6 blocker).
        Assert.Null(download.Content.Headers.ContentDisposition);
    }

    [Fact]
    public async Task The_download_preflight_allows_the_proof_header_for_the_web_origin()
    {
        using var api = host.CreateApi();
        var token = await host.AdminTokenAsync();
        var record = await DemoRecordPathAsync(api, token);
        var id = await LinkedIdAsync(api, record, (await UploadAsync(api, record, token)).Receipt, token);

        await using var twin = BrowserTwinHost.Start(host);
        using var browser = twin.CreateBrowser();
        using var preflight = await browser.PreflightAsync(AttachmentsPath + id + ContentSuffix, BrowserTwinHost.WebOrigin);

        Assert.Equal(HttpStatusCode.NoContent, preflight.StatusCode);
        Assert.Equal([BrowserTwinHost.WebOrigin], preflight.Headers.GetValues(HeaderNames.AccessControlAllowOrigin));
        var allowed = HeaderList(preflight, HeaderNames.AccessControlAllowHeaders);
        Assert.Contains(HeaderNames.Authorization, allowed, StringComparer.OrdinalIgnoreCase);
        Assert.Contains(host.Service<SecurityProtocol>().SignatureHeaderName, allowed, StringComparer.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Cross_origin_download_refusals_still_carry_the_cors_headers()
    {
        using var api = host.CreateApi();
        var token = await host.AdminTokenAsync();
        var record = await DemoRecordPathAsync(api, token);
        var id = await LinkedIdAsync(api, record, (await UploadAsync(api, record, token)).Receipt, token);
        using (var removed = await DeleteAsync(AttachmentsPath + id, token))
        {
            Assert.Equal(HttpStatusCode.NoContent, removed.StatusCode);
        }

        await using var twin = BrowserTwinHost.Start(host);
        using var browser = twin.CreateBrowser();
        var twinToken = await twin.AdminTokenAsync();
        using var unproven = await browser.GetWithoutProofAsync(AttachmentsPath + id + ContentSuffix, twinToken, BrowserTwinHost.WebOrigin);
        using var gone = await browser.GetAsync(AttachmentsPath + id + ContentSuffix, twinToken, BrowserTwinHost.WebOrigin);

        var refusal = await ApiClient.ReadAsync(unproven, HttpStatusCode.Unauthorized);
        Assert.Equal(SecurityProblemCodes.SignatureMissing, refusal.GetProperty("code").GetString());
        Assert.Equal([BrowserTwinHost.WebOrigin], unproven.Headers.GetValues(HeaderNames.AccessControlAllowOrigin));
        Assert.Equal(HttpStatusCode.NotFound, gone.StatusCode);
        Assert.Equal([BrowserTwinHost.WebOrigin], gone.Headers.GetValues(HeaderNames.AccessControlAllowOrigin));
        Assert.Contains(host.Service<SecurityProtocol>().ResponseSignatureHeaderName, ExposedHeaders(gone), StringComparer.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task The_interlock_refuses_the_demo_record_until_its_required_file_is_linked()
    {
        using var api = host.CreateApi();
        var token = await host.AdminTokenAsync();
        var record = await DemoRecordPathAsync(api, token);
        foreach (var linked in await LinkedIdsAsync(api, record, token))
        {
            using var removed = await DeleteAsync(AttachmentsPath + linked, token);
            Assert.Equal(HttpStatusCode.NoContent, removed.StatusCode);
        }

        var before = await host.SubmitRefusalAsync(DemoRecord.RecordType, DemoRecord.RecordId);
        var upload = await UploadAsync(api, record, token);
        await LinkedIdAsync(api, record, upload.Receipt, token);
        var after = await host.SubmitRefusalAsync(DemoRecord.RecordType, DemoRecord.RecordId);

        Assert.Equal((RefusalCategory.Conflict, AttachmentErrorCodes.RequiredMissing), (before!.Category, before.Code));
        Assert.Null(after);
    }

    [Fact]
    public async Task Remove_answers_204_hides_the_file_and_enqueues_its_deletion()
    {
        using var api = host.CreateApi();
        var token = await host.AdminTokenAsync();
        var record = await DemoRecordPathAsync(api, token);
        var upload = await UploadAsync(api, record, token);
        var id = await LinkedIdAsync(api, record, upload.Receipt, token);

        using var removed = await DeleteAsync(AttachmentsPath + id, token);

        Assert.Equal(HttpStatusCode.NoContent, removed.StatusCode);
        Assert.DoesNotContain(id, await LinkedIdsAsync(api, record, token));
        Assert.Equal(1, await PendingDeletionsAsync(upload.FileId));
        await host.DrainOutboxAsync();
        Assert.Contains(upload.FileId, host.Storage.Deleted);
    }

    private static async Task<string> DemoRecordPathAsync(ApiClient api, string token)
    {
        using var response = await api.GetAsync(DemoRecordPath, token);
        var demo = await ApiClient.ReadAsync(response, HttpStatusCode.OK);
        Assert.Equal(AttachableRecordTypes.Demo, demo.GetProperty("recordType").GetString());
        return "/api/records/" + AttachableRecordTypes.Demo + "/" + demo.GetProperty("recordId").GetString() + "/attachments";
    }

    private static async Task<JsonElement> IssueTicketAsync(ApiClient api, string record, string token)
    {
        using var response = await api.PostAsync(record + "/upload-ticket", new { typeKey = AttachmentTypeKeys.DemoDocument }, token);
        return await ApiClient.ReadAsync(response, HttpStatusCode.OK);
    }

    /// <summary>
    /// What the attachment service does for one file: opens the ticket (once), stores the bytes, and signs a receipt
    /// whose scope is the ticket subject (or <paramref name="rescope"/> of it, to forge a foreign receipt).
    /// </summary>
    private async Task<Uploaded> UploadAsync(ApiClient api, string record, string token, Func<string, string>? rescope = null)
    {
        var ticket = (await IssueTicketAsync(api, record, token)).GetProperty("ticket").GetString()!;
        var opened = await host.Service<UploadTicketRing>().OpenAsync(UploadTickets.Purpose, ticket, CancellationToken.None);
        Assert.Equal(TicketOpenStatus.Valid, opened.Status);

        var fileId = Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(FileIdBytes));
        var bytes = Encoding.UTF8.GetBytes("%PDF-1.7 " + fileId);
        host.Storage.Put(fileId, bytes);
        var scope = rescope is null ? opened.Ticket!.Subject : rescope(opened.Ticket!.Subject);
        var receipt = host.Service<IUploadReceiptSigner>().Sign(new UploadReceipt(
            Scope: scope,
            FileId: fileId,
            SizeBytes: bytes.Length,
            Sha256Hex: Convert.ToHexStringLower(SHA256.HashData(bytes)),
            FileName: FileName,
            IssuedAtUnix: TimeProvider.System.GetUtcNow().ToUnixTimeSeconds()));
        return new Uploaded(fileId, bytes, receipt);
    }

    private static Task<HttpResponseMessage> LinkAsync(ApiClient api, string record, string receipt, string token) =>
        api.PostAsync(record, new { typeKey = AttachmentTypeKeys.DemoDocument, receipt }, token);

    private static async Task<string> LinkedIdAsync(ApiClient api, string record, string receipt, string token)
    {
        using var linked = await LinkAsync(api, record, receipt, token);
        return (await ApiClient.ReadAsync(linked, HttpStatusCode.Created)).GetProperty("id").GetString()!;
    }

    private static async Task<IReadOnlyList<string>> LinkedIdsAsync(ApiClient api, string record, string token)
    {
        using var response = await api.GetAsync(record, token);
        var body = await ApiClient.ReadAsync(response, HttpStatusCode.OK);
        return [.. body.GetProperty("items").EnumerateArray().Select(item => item.GetProperty("id").GetString()!)];
    }

    private async Task<HttpResponseMessage> DeleteAsync(string path, string token)
    {
        using var http = host.Factory.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Delete, new Uri(path, UriKind.Relative));
        request.Headers.Authorization = new AuthenticationHeaderValue(BearerScheme, token);
        return await http.SendAsync(request);
    }

    private async Task<int> PendingDeletionsAsync(string fileId)
    {
        const string sql = "SELECT COUNT(*) FROM [messaging].[OutboxMessages] WHERE [Type] = @type AND [PayloadJson] LIKE @file";
        var count = await TestSqlServer.ScalarAsync(host.DatabaseName, sql, new Dictionary<string, object>
        {
            ["@type"] = AttachmentsOutboxMessageTypes.DeleteFile,
            ["@file"] = "%" + fileId + "%",
        });
        return Convert.ToInt32(count, CultureInfo.InvariantCulture);
    }

    /// <summary>The comma-separated values of <paramref name="name"/>, trimmed (empty when absent).</summary>
    private static IReadOnlyList<string> HeaderList(HttpResponseMessage response, string name) =>
        response.Headers.TryGetValues(name, out var values)
            ? [.. values.SelectMany(value => value.Split(HeaderListSeparator, StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))]
            : [];

    private static IReadOnlyList<string> ExposedHeaders(HttpResponseMessage response) =>
        HeaderList(response, HeaderNames.AccessControlExposeHeaders);

    /// <summary>The receipt with its payload changed (a larger size) and the original MAC kept.</summary>
    private static string Tampered(string receipt)
    {
        var parts = receipt.Split(ReceiptSeparator);
        var original = JsonSerializer.Deserialize<UploadReceipt>(Base64Url.DecodeFromChars(parts[ReceiptPayloadPart]))!;
        var forged = original with { SizeBytes = original.SizeBytes + 1 };
        parts[ReceiptPayloadPart] = Base64Url.EncodeToString(JsonSerializer.SerializeToUtf8Bytes(forged));
        return string.Join(ReceiptSeparator, parts);
    }

    /// <summary>The same scope for the next record (its PublicId: the receipt of another record).</summary>
    private string OtherRecordScope(string scope)
    {
        var parts = scope.Split(ScopeSeparator);
        parts[RecordIdScopePart] = host.Service<IPublicIdCodec>().Protect(AttachableRecordIds.Domain, OtherRecordId);
        return string.Join(ScopeSeparator, parts);
    }

    private sealed record Uploaded(string FileId, byte[] Bytes, string Receipt);
}
