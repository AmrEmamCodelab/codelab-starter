using System.Net;
using Microsoft.Extensions.DependencyInjection;
using Starter.Application.Records;
using Starter.Integration.Tests.Access;
using Starter.Integration.Tests.Support;
using Starter.Tests.Hosting;

namespace Starter.Integration.Tests.Assignments;

/// <summary>
/// Module 9 on real SQL through the real host: the work queue of the test-only record type (an in-memory record
/// directory stands in for the Records module), claim / release with their refusals, two claims racing on the
/// one-open-row index, organization scope, the queue permission gate, the fail-closed assignee check, and the terminal
/// auto-release through the kernel transition observers. Assignment rows are read back from <c>assignments.RecordAssignments</c>.
/// </summary>
[Collection(AssignmentsHttpHostGroup.Name)]
public sealed class QueueHttpTests(AssignmentsHttpHost host) : IDisposable
{
    private const string ReleaseReasonSql =
        "SELECT TOP 1 ReleaseReason FROM assignments.RecordAssignments WHERE RecordId = @recordId AND ReleasedAt IS NOT NULL ORDER BY Id DESC";

    private const string OpenRowsSql =
        "SELECT COUNT(*) FROM assignments.RecordAssignments WHERE RecordId = @recordId AND ReleasedAt IS NULL";

    private const string RowsSql = "SELECT COUNT(*) FROM assignments.RecordAssignments WHERE RecordId = @recordId";
    private const int ReleasedReason = 1;
    private const int RecordClosedReason = 3;

    private readonly QueueApi _api = new(host);

    [Fact]
    public async Task A_worker_claims_a_record_a_second_claim_is_refused_and_only_the_assignee_releases_it()
    {
        var (worker, colleague, organizationId) = await TwoWorkersAsync();
        var recordId = QueueApi.NextRecordId();
        _api.PutRecord(recordId, ownerUserId: colleague.UserId, organizationId: organizationId);

        using var claimed = await _api.ClaimAsync(worker, recordId);
        using var claimedAgain = await _api.ClaimAsync(colleague, recordId);
        using var mine = await _api.ListAsync(worker, "view=" + QueueApi.Mine);
        using var foreignRelease = await _api.ReleaseAsync(colleague, recordId);
        using var released = await _api.ReleaseAsync(worker, recordId);
        using var unassigned = await _api.ListAsync(colleague, "view=" + QueueApi.Unassigned + "&pageSize=100");

        Assert.Equal(HttpStatusCode.NoContent, claimed.StatusCode);
        Assert.Equal("ASSIGNMENTS_ALREADY_CLAIMED", await StarterProblems.AssertProblemAsync(claimedAgain, HttpStatusCode.Conflict));
        var (mineIds, mineBody) = await QueueApi.ReadPageAsync(mine);
        Assert.Equal([_api.RecordPublicId(recordId)], mineIds);
        var item = mineBody.GetProperty("items")[0];
        Assert.Equal(worker.Id, item.GetProperty("assignee").GetProperty("userId").GetString());
        Assert.Equal(worker.DisplayName, item.GetProperty("assignee").GetProperty("displayName").GetString());
        Assert.Equal(colleague.DisplayName, item.GetProperty("ownerDisplayName").GetString());
        Assert.Equal("submitted", item.GetProperty("statusKey").GetString());
        Assert.Equal(TestRecord.Submitted, item.GetProperty("status").GetInt32());
        Assert.Equal("ASSIGNMENTS_NOT_ASSIGNEE", await StarterProblems.AssertProblemAsync(foreignRelease, HttpStatusCode.Forbidden));
        Assert.Equal(HttpStatusCode.NoContent, released.StatusCode);
        Assert.Contains(_api.RecordPublicId(recordId), (await QueueApi.ReadPageAsync(unassigned)).Ids);
        Assert.Equal(ReleasedReason, await ScalarAsync(ReleaseReasonSql, recordId));
        Assert.Equal(0, await ScalarAsync(OpenRowsSql, recordId));
    }

    [Fact]
    public async Task Two_claims_at_the_same_moment_leave_exactly_one_open_row()
    {
        var (worker, colleague, organizationId) = await TwoWorkersAsync();
        var recordId = QueueApi.NextRecordId();
        _api.PutRecord(recordId, ownerUserId: worker.UserId, organizationId: organizationId);

        var responses = await Task.WhenAll(_api.ClaimAsync(worker, recordId), _api.ClaimAsync(colleague, recordId));

        try
        {
            Assert.Single(responses, response => response.StatusCode == HttpStatusCode.NoContent);
            Assert.Single(responses, response => response.StatusCode == HttpStatusCode.Conflict);
            Assert.Equal(1, await ScalarAsync(OpenRowsSql, recordId));
            Assert.Equal(1, await ScalarAsync(RowsSql, recordId));
        }
        finally
        {
            foreach (var response in responses)
            {
                response.Dispose();
            }
        }
    }

    [Fact]
    public async Task An_own_organization_worker_sees_and_claims_only_their_organizations_records()
    {
        var suffix = QueueApi.Suffix();
        var own = await _api.CreateOrganizationAsync("QA-" + suffix);
        var other = await _api.CreateOrganizationAsync("QB-" + suffix);
        var worker = await _api.CreateStaffAsync(
            "queue.scoped." + suffix,
            own.Id,
            QueueApi.Grant(QueueApi.WorkPermission),
            QueueApi.Grant(TestRecord.QueuePermission, AccessApi.Scopes.OwnOrganization));
        var ownRecord = QueueApi.NextRecordId();
        var otherRecord = QueueApi.NextRecordId();
        _api.PutRecord(ownRecord, ownerUserId: worker.UserId, organizationId: own.OrganizationId);
        _api.PutRecord(otherRecord, ownerUserId: worker.UserId, organizationId: other.OrganizationId);

        using var listed = await _api.ListAsync(worker, "view=" + QueueApi.All + "&pageSize=100");
        using var foreignClaim = await _api.ClaimAsync(worker, otherRecord);

        var ids = (await QueueApi.ReadPageAsync(listed)).Ids;
        Assert.Contains(_api.RecordPublicId(ownRecord), ids);
        Assert.DoesNotContain(_api.RecordPublicId(otherRecord), ids);
        Assert.Equal("RECORDS_NOT_FOUND", await StarterProblems.AssertProblemAsync(foreignClaim, HttpStatusCode.NotFound));
    }

    [Fact]
    public async Task The_queue_needs_both_the_work_permission_and_the_record_types_queue_permission()
    {
        var suffix = QueueApi.Suffix();
        var organization = await _api.CreateOrganizationAsync("QP-" + suffix);
        var workOnly = await _api.CreateStaffAsync("queue.work-only." + suffix, organization.Id, QueueApi.Grant(QueueApi.WorkPermission));
        var queueOnly = await _api.CreateStaffAsync("queue.queue-only." + suffix, organization.Id, QueueApi.Grant(TestRecord.QueuePermission));

        using var withoutQueue = await _api.ListAsync(workOnly, "view=" + QueueApi.All);
        using var withoutWork = await _api.ListAsync(queueOnly, "view=" + QueueApi.All);
        using var unknownType = await _api.ListAsync(workOnly, "view=" + QueueApi.All, recordType: "no-such-record");

        Assert.Equal("ASSIGNMENTS_QUEUE_FORBIDDEN", await StarterProblems.AssertProblemAsync(withoutQueue, HttpStatusCode.Forbidden));
        await StarterProblems.AssertProblemAsync(withoutWork, HttpStatusCode.Forbidden);
        Assert.Equal("ASSIGNMENTS_RECORD_TYPE_UNKNOWN", await StarterProblems.AssertProblemAsync(unknownType, HttpStatusCode.NotFound));
    }

    [Fact]
    public async Task Invalid_list_requests_and_drafts_are_refused()
    {
        var (worker, _, organizationId) = await TwoWorkersAsync();
        var draft = QueueApi.NextRecordId();
        _api.PutRecord(draft, ownerUserId: worker.UserId, organizationId: organizationId, status: TestRecord.Draft);

        using var noView = await _api.ListAsync(worker, "page=1");
        using var badStatus = await _api.ListAsync(worker, "view=" + QueueApi.All + "&status=" + TestRecord.Closed);
        using var draftClaim = await _api.ClaimAsync(worker, draft);

        Assert.Equal("VALIDATION_FAILED", await StarterProblems.AssertProblemAsync(noView, HttpStatusCode.BadRequest));
        Assert.Equal("VALIDATION_FAILED", await StarterProblems.AssertProblemAsync(badStatus, HttpStatusCode.BadRequest));
        Assert.Equal("ASSIGNMENTS_NOT_ASSIGNABLE", await StarterProblems.AssertProblemAsync(draftClaim, HttpStatusCode.Conflict));
    }

    [Fact]
    public async Task An_assignment_is_refused_while_no_assignee_check_is_wired_and_needs_a_user_id()
    {
        var suffix = QueueApi.Suffix();
        var organization = await _api.CreateOrganizationAsync("QS-" + suffix);
        var supervisor = await _api.CreateStaffAsync(
            "queue.supervisor." + suffix,
            organization.Id,
            QueueApi.Grant(QueueApi.WorkPermission),
            QueueApi.Grant(QueueApi.AssignPermission),
            QueueApi.Grant(TestRecord.QueuePermission));
        var worker = await _api.CreateStaffAsync(
            "queue.assignee." + suffix, organization.Id, QueueApi.Grant(QueueApi.WorkPermission), QueueApi.Grant(TestRecord.QueuePermission));
        var recordId = QueueApi.NextRecordId();
        _api.PutRecord(recordId, ownerUserId: worker.UserId, organizationId: organization.OrganizationId);

        using var assigned = await _api.AssignAsync(supervisor, recordId, new { userId = worker.Id });
        using var withoutUser = await _api.AssignAsync(supervisor, recordId, new { });
        using var byWorker = await _api.AssignAsync(worker, recordId, new { userId = worker.Id });

        Assert.Equal("ASSIGNMENTS_ASSIGNEE_NOT_ELIGIBLE", await StarterProblems.AssertProblemAsync(assigned, HttpStatusCode.BadRequest));
        Assert.Equal("VALIDATION_FAILED", await StarterProblems.AssertProblemAsync(withoutUser, HttpStatusCode.BadRequest));
        await StarterProblems.AssertProblemAsync(byWorker, HttpStatusCode.Forbidden);
        Assert.Equal(0, await ScalarAsync(RowsSql, recordId));
    }

    [Fact]
    public async Task A_terminal_transition_closes_the_assignment_and_the_closed_record_leaves_the_queue()
    {
        var (worker, _, organizationId) = await TwoWorkersAsync();
        var recordId = QueueApi.NextRecordId();
        var record = _api.PutRecord(recordId, ownerUserId: worker.UserId, organizationId: organizationId, status: TestRecord.InReview);
        using (var claimed = await _api.ClaimAsync(worker, recordId))
        {
            Assert.Equal(HttpStatusCode.NoContent, claimed.StatusCode);
        }

        host.Directory.Put(record with { Status = TestRecord.Closed, IsTerminal = true });
        await NotifyObserversAsync(new RecordTransitioned(
            RecordId: recordId,
            RecordType: TestRecord.Type,
            FromStatus: TestRecord.InReview,
            ToStatus: TestRecord.Closed,
            ActionKey: "close",
            ActorUserId: worker.UserId,
            ToTerminal: true,
            At: host.Service<TimeProvider>().GetUtcNow()));
        using var mine = await _api.ListAsync(worker, "view=" + QueueApi.Mine);
        using var reclaimed = await _api.ClaimAsync(worker, recordId);

        Assert.Equal(RecordClosedReason, await ScalarAsync(ReleaseReasonSql, recordId));
        Assert.Equal(0, await ScalarAsync(OpenRowsSql, recordId));
        Assert.Empty((await QueueApi.ReadPageAsync(mine)).Ids);
        Assert.Equal("ASSIGNMENTS_NOT_ASSIGNABLE", await StarterProblems.AssertProblemAsync(reclaimed, HttpStatusCode.Conflict));
    }

    [Fact]
    public async Task The_host_exposes_the_assignment_reader_to_the_record_views()
    {
        var (worker, _, organizationId) = await TwoWorkersAsync();
        var recordId = QueueApi.NextRecordId();
        _api.PutRecord(recordId, ownerUserId: worker.UserId, organizationId: organizationId);
        using (var claimed = await _api.ClaimAsync(worker, recordId))
        {
            Assert.Equal(HttpStatusCode.NoContent, claimed.StatusCode);
        }

        await using var scope = host.Factory.Services.CreateAsyncScope();
        var readers = scope.ServiceProvider.GetServices<IRecordAssignmentReader>().ToList();
        var current = await Assert.Single(readers).CurrentAsync([recordId], CancellationToken.None);

        Assert.Equal(worker.UserId, current[recordId].AssigneeUserId);
        Assert.Equal(worker.DisplayName, current[recordId].AssigneeDisplayName);
    }

    public void Dispose() => _api.Dispose();

    private async Task<(QueueStaff Worker, QueueStaff Colleague, long OrganizationId)> TwoWorkersAsync()
    {
        var suffix = QueueApi.Suffix();
        var organization = await _api.CreateOrganizationAsync("QW-" + suffix);
        var grants = new[] { QueueApi.Grant(QueueApi.WorkPermission), QueueApi.Grant(TestRecord.QueuePermission) };
        var worker = await _api.CreateStaffAsync("queue.worker." + suffix, organization.Id, grants);
        var colleague = await _api.CreateStaffAsync("queue.colleague." + suffix, organization.Id, grants);
        return (worker, colleague, organization.OrganizationId);
    }

    private async Task NotifyObserversAsync(RecordTransitioned transition)
    {
        await using var scope = host.Factory.Services.CreateAsyncScope();
        foreach (var observer in scope.ServiceProvider.GetServices<IRecordTransitionObserver>())
        {
            await observer.OnTransitionedAsync(transition, CancellationToken.None);
        }
    }

    private async Task<int> ScalarAsync(string sql, long recordId) =>
        Convert.ToInt32(
            await TestSqlServer.ScalarAsync(host.DatabaseName, sql, new Dictionary<string, object> { ["@recordId"] = recordId }),
            System.Globalization.CultureInfo.InvariantCulture);
}
