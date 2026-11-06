using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Codelab.Security.Tickets;
using Codelab.Security.Uploads;
using Microsoft.Data.SqlClient;
using Starter.Application.Attachments;
using Starter.Application.Records;
using Starter.Application.Workflow;
using Starter.Integration.Tests.Support;
using Starter.Modules.Attachments.Application;
using Starter.Modules.Attachments.Infrastructure.Security;
using Starter.Modules.Records.Application;
using Starter.Tests.Hosting;

namespace Starter.Integration.Tests.Records;

/// <summary>
/// The generic Records flows over HTTP on real SQL with the test-only record type <see cref="ProbeRecord"/>: draft ->
/// submit refused 409 ATTACHMENTS_REQUIRED_MISSING until the required file is linked through the real attachments flow
/// (phase 6 carry-over), the PREFIX-YYYY-NNN number, a mandatory reason (400), an action from the wrong status (409), a
/// terminal record offering nothing, the RecordHistory foreign key refusing a delete (Restrict), and the read-only
/// oversight list.
/// </summary>
[Collection(RecordsHostGroup.Name)]
public sealed partial class RecordsHttpTests(RecordsHost host)
{
    private const string Root = "/api/records/" + ProbeRecord.RecordType;
    private const string DraftsPath = "/api/test-records";
    private const string FileName = "evidence.pdf";
    private const int FileIdBytes = 16;

    /// <summary>SQL Server's error number for a statement that conflicts with a constraint (here the history FK).</summary>
    private const int ConstraintConflict = 547;

    [Fact]
    public async Task Submit_is_refused_409_until_the_required_file_is_linked_then_takes_the_number()
    {
        await host.ReadyAsync();
        using var api = host.CreateApi();
        var token = await host.RequesterTokenAsync();
        var id = await DraftAsync(api, token);

        using var refused = await api.PostAsync($"{DraftsPath}/{id}/submit", body: null, token);
        Assert.Equal(AttachmentErrorCodes.RequiredMissing, await StarterProblems.AssertProblemAsync(refused, HttpStatusCode.Conflict));
        Assert.Equal(ProbeRecord.Draft, (await DetailAsync(api, id, token)).GetProperty("status").GetInt32());

        await LinkRequiredFileAsync(api, await DetailAsync(api, id, token), token);
        using var submitted = await api.PostAsync($"{DraftsPath}/{id}/submit", body: null, token);
        var number = (await ApiClient.ReadAsync(submitted, HttpStatusCode.OK)).GetProperty("number").GetString()!;

        Assert.Matches(NumberFormat(), number);
        var detail = await DetailAsync(api, id, token);
        Assert.Equal(ProbeRecord.Submitted, detail.GetProperty("status").GetInt32());
        Assert.Equal(number, detail.GetProperty("number").GetString());
        Assert.Equal("submitted", detail.GetProperty("statusKey").GetString());
    }

    [Fact]
    public async Task The_generic_transition_also_refuses_a_submit_without_the_required_file()
    {
        await host.ReadyAsync();
        using var api = host.CreateApi();
        var token = await host.RequesterTokenAsync();
        var id = await DraftAsync(api, token);

        using var refused = await TransitionAsync(api, id, RecordLifecycleActions.Submit, reason: null, token);

        Assert.Equal(AttachmentErrorCodes.RequiredMissing, await StarterProblems.AssertProblemAsync(refused, HttpStatusCode.Conflict));
    }

    [Fact]
    public async Task A_return_without_its_mandatory_reason_is_400_and_with_it_reaches_the_requester()
    {
        var (api, id, requester, staff) = await InReviewAsync();
        using (api)
        {
            using var noReason = await TransitionAsync(api, id, ProbeRecord.Return, reason: "  ", staff);
            Assert.Equal(WorkflowErrorCodes.ReasonRequired, await StarterProblems.AssertProblemAsync(noReason, HttpStatusCode.BadRequest));

            using var returned = await TransitionAsync(api, id, ProbeRecord.Return, "The page is unreadable", staff);
            var detail = await ApiClient.ReadAsync(returned, HttpStatusCode.OK);
            Assert.Equal(ProbeRecord.Returned, detail.GetProperty("status").GetInt32());

            var requesterView = await DetailAsync(api, id, requester);
            Assert.Equal("The page is unreadable", requesterView.GetProperty("lastReason").GetString());
            Assert.Equal([RecordLifecycleActions.Resubmit], ActionKeys(requesterView));
            Assert.Contains(host.Notifications.Sent, sent => sent.TemplateKey == ProbeRecord.ReturnedTemplate);
        }
    }

    [Fact]
    public async Task An_action_from_the_wrong_status_is_409_and_an_unknown_action_404()
    {
        var (api, id, _, staff) = await InReviewAsync();
        using (api)
        {
            using var again = await TransitionAsync(api, id, ProbeRecord.Take, reason: null, staff);
            Assert.Equal(WorkflowErrorCodes.ActionNotAllowed, await StarterProblems.AssertProblemAsync(again, HttpStatusCode.Conflict));

            using var unknown = await TransitionAsync(api, id, "teleport", reason: null, staff);
            Assert.Equal(WorkflowErrorCodes.ActionUnknown, await StarterProblems.AssertProblemAsync(unknown, HttpStatusCode.NotFound));
        }
    }

    [Fact]
    public async Task A_terminal_record_offers_no_actions_and_refuses_more()
    {
        var (api, id, requester, staff) = await InReviewAsync();
        using (api)
        {
            using var accepted = await TransitionAsync(api, id, ProbeRecord.Accept, reason: null, staff);
            var detail = await ApiClient.ReadAsync(accepted, HttpStatusCode.OK);

            Assert.True(detail.GetProperty("isTerminal").GetBoolean());
            Assert.Empty(ActionKeys(detail));
            Assert.Empty(ActionKeys(await DetailAsync(api, id, requester)));
            using var cancel = await TransitionAsync(api, id, RecordLifecycleActions.Cancel, reason: null, requester);
            Assert.Equal(WorkflowErrorCodes.ActionNotAllowed, await StarterProblems.AssertProblemAsync(cancel, HttpStatusCode.Conflict));
        }
    }

    [Fact]
    public async Task The_timeline_shows_every_step_with_its_actor_and_side()
    {
        var (api, id, requester, _) = await InReviewAsync();
        using (api)
        {
            using var response = await api.GetAsync($"{Root}/{id}/timeline", requester);
            var timeline = (await ApiClient.ReadAsync(response, HttpStatusCode.OK)).EnumerateArray().ToList();

            Assert.Equal([RecordLifecycleActions.Create, RecordLifecycleActions.Submit, ProbeRecord.Take], timeline.Select(entry => entry.GetProperty("actionKey").GetString()));
            Assert.Equal(JsonValueKind.Null, timeline[0].GetProperty("fromStatus").ValueKind);
            Assert.Equal([1, 1, 2], timeline.Select(entry => entry.GetProperty("side").GetInt32()));
            Assert.All(timeline, entry => Assert.False(string.IsNullOrWhiteSpace(entry.GetProperty("actorDisplayName").GetString())));
            Assert.Equal("in-review", timeline[^1].GetProperty("toStatusKey").GetString());
        }
    }

    [Fact]
    public async Task Deleting_a_record_with_history_is_refused_by_the_restrict_foreign_key()
    {
        await host.ReadyAsync();
        using var api = host.CreateApi();
        var token = await host.RequesterTokenAsync();
        await DraftAsync(api, token);
        var recordId = Convert.ToInt64(await TestSqlServer.ScalarAsync(host.DatabaseName, "SELECT MAX([Id]) FROM [records].[Records]"), System.Globalization.CultureInfo.InvariantCulture);

        var refused = await Assert.ThrowsAsync<SqlException>(() => TestSqlServer.ExecuteAsync(
            host.DatabaseName, "DELETE FROM [records].[Records] WHERE [Id] = @id", new Dictionary<string, object> { ["@id"] = recordId }));

        Assert.Equal(ConstraintConflict, refused.Number);
        Assert.Equal(1, Convert.ToInt32(
            await TestSqlServer.ScalarAsync(host.DatabaseName, "SELECT COUNT(*) FROM [records].[Records] WHERE [Id] = @id", new Dictionary<string, object> { ["@id"] = recordId }),
            System.Globalization.CultureInfo.InvariantCulture));
        Assert.Equal("NO_ACTION", await TestSqlServer.ScalarAsync(
            host.DatabaseName,
            "SELECT [delete_referential_action_desc] FROM sys.foreign_keys WHERE [parent_object_id] = OBJECT_ID(@table)",
            new Dictionary<string, object> { ["@table"] = "records.RecordHistory" }));
    }

    [Fact]
    public async Task Oversight_is_read_only_and_needs_its_permission()
    {
        var (api, id, requester, staff) = await InReviewAsync();
        using (api)
        {
            using var response = await api.GetAsync($"{Root}/oversight?pageSize=100", staff);
            var page = await ApiClient.ReadAsync(response, HttpStatusCode.OK);
            var item = page.GetProperty("items").EnumerateArray().Single(candidate => candidate.GetProperty("id").GetString() == id);

            Assert.False(item.TryGetProperty("allowedActions", out _));
            Assert.False(string.IsNullOrWhiteSpace(item.GetProperty("ownerDisplayName").GetString()));
            using var denied = await api.GetAsync($"{Root}/oversight", requester);
            await StarterProblems.AssertProblemAsync(denied, HttpStatusCode.Forbidden);
            using var write = await api.PostAsync($"{Root}/oversight", new { action = ProbeRecord.Accept }, staff);
            Assert.True(write.StatusCode is HttpStatusCode.NotFound or HttpStatusCode.MethodNotAllowed, $"Oversight accepted a write: {(int)write.StatusCode}");
        }
    }

    [Fact]
    public async Task My_records_lists_the_callers_records_and_needs_the_view_own_permission()
    {
        await host.ReadyAsync();
        using var api = host.CreateApi();
        var token = await host.RequesterTokenAsync();
        var id = await DraftAsync(api, token);

        using var mine = await api.GetAsync($"{Root}/mine?status={ProbeRecord.Draft}&pageSize=100", token);
        var page = await ApiClient.ReadAsync(mine, HttpStatusCode.OK);

        Assert.Contains(page.GetProperty("items").EnumerateArray(), item => item.GetProperty("id").GetString() == id);
        Assert.Equal(JsonValueKind.Null, page.GetProperty("items")[0].GetProperty("number").ValueKind);
        using var staffMine = await api.GetAsync($"{Root}/mine", await host.StaffTokenAsync());
        Assert.Equal(HttpStatusCode.OK, staffMine.StatusCode);
        using var unknownType = await api.GetAsync("/api/records/no-such-type/mine", token);
        Assert.Equal(RecordsErrorCodes.NotFound, await StarterProblems.AssertProblemAsync(unknownType, HttpStatusCode.NotFound));
    }

    [Fact]
    public async Task A_draft_is_hidden_from_staff_until_it_is_submitted()
    {
        await host.ReadyAsync();
        using var api = host.CreateApi();
        var token = await host.RequesterTokenAsync();
        var id = await DraftAsync(api, token);

        using var hidden = await api.GetAsync($"{Root}/{id}", await host.StaffTokenAsync());

        Assert.Equal(RecordsErrorCodes.NotFound, await StarterProblems.AssertProblemAsync(hidden, HttpStatusCode.NotFound));
    }

    private async Task<(ApiClient Api, string Id, string Requester, string Staff)> InReviewAsync()
    {
        await host.ReadyAsync();
        var api = host.CreateApi();
        var requester = await host.RequesterTokenAsync();
        var staff = await host.StaffTokenAsync();
        var id = await DraftAsync(api, requester);
        await LinkRequiredFileAsync(api, await DetailAsync(api, id, requester), requester);
        using (var submitted = await TransitionAsync(api, id, RecordLifecycleActions.Submit, reason: null, requester))
        {
            await ApiClient.ReadAsync(submitted, HttpStatusCode.OK);
        }

        using (var taken = await TransitionAsync(api, id, ProbeRecord.Take, reason: null, staff))
        {
            Assert.Equal(ProbeRecord.InReview, (await ApiClient.ReadAsync(taken, HttpStatusCode.OK)).GetProperty("status").GetInt32());
        }

        return (api, id, requester, staff);
    }

    private static async Task<string> DraftAsync(ApiClient api, string token)
    {
        using var created = await api.PostAsync(DraftsPath, body: null, token);
        return (await ApiClient.ReadAsync(created, HttpStatusCode.Created)).GetProperty("id").GetString()!;
    }

    private static async Task<JsonElement> DetailAsync(ApiClient api, string id, string token)
    {
        using var response = await api.GetAsync($"{Root}/{id}", token);
        return await ApiClient.ReadAsync(response, HttpStatusCode.OK);
    }

    private static Task<HttpResponseMessage> TransitionAsync(ApiClient api, string id, string action, string? reason, string token) =>
        api.PostAsync($"{Root}/{id}/transitions", new { action, reason }, token);

    private static IReadOnlyList<string> ActionKeys(JsonElement detail) =>
        [.. detail.GetProperty("allowedActions").EnumerateArray().Select(action => action.GetProperty("actionKey").GetString()!)];

    /// <summary>What the browser and the attachment service do for the required file: ticket, upload, signed receipt, link.</summary>
    private async Task LinkRequiredFileAsync(ApiClient api, JsonElement detail, string token)
    {
        var record = $"/api/records/{detail.GetProperty("attachableRecordType").GetString()}/{detail.GetProperty("attachmentsRecordId").GetString()}/attachments";
        using var ticketResponse = await api.PostAsync(record + "/upload-ticket", new { typeKey = ProbeRecord.RequiredDocumentKey }, token);
        var ticket = (await ApiClient.ReadAsync(ticketResponse, HttpStatusCode.OK)).GetProperty("ticket").GetString()!;
        var opened = await host.Service<UploadTicketRing>().OpenAsync(UploadTickets.Purpose, ticket, CancellationToken.None);
        Assert.Equal(TicketOpenStatus.Valid, opened.Status);

        var fileId = Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(FileIdBytes));
        var bytes = Encoding.UTF8.GetBytes("%PDF-1.7 " + fileId);
        host.Storage.Put(fileId, bytes);
        var receipt = host.Service<IUploadReceiptSigner>().Sign(new UploadReceipt(
            Scope: opened.Ticket!.Subject,
            FileId: fileId,
            SizeBytes: bytes.Length,
            Sha256Hex: Convert.ToHexStringLower(SHA256.HashData(bytes)),
            FileName: FileName,
            IssuedAtUnix: TimeProvider.System.GetUtcNow().ToUnixTimeSeconds()));
        using var linked = await api.PostAsync(record, new { typeKey = ProbeRecord.RequiredDocumentKey, receipt }, token);
        await ApiClient.ReadAsync(linked, HttpStatusCode.Created);
    }

    [GeneratedRegex(@"^TR-\d{4}-\d{3,}$", RegexOptions.CultureInvariant)]
    private static partial Regex NumberFormat();
}
