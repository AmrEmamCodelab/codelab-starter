using Starter.Application.Authorization;
using Starter.Application.Records;
using Starter.Application.Results;
using Starter.Domain.Workflow;
using Starter.Modules.Records.Application;
using Starter.Modules.Records.Tests.Support;
using Starter.Workflow.Testing;

namespace Starter.Modules.Records.Tests.Application;

public sealed class RecordReadsTests
{
    private readonly RecordsHarness _harness = new();

    [Fact]
    public async Task The_requester_detail_offers_the_requester_actions_of_the_status()
    {
        var reference = await _harness.DraftAsync();

        var detail = await DetailAsync(reference);

        Assert.Equal(WorkflowAudience.Requester, detail.Audience);
        Assert.Equal([TestRecordActions.Submit, RecordLifecycleActions.Cancel], detail.AllowedActions.Select(action => action.ActionKey));
        Assert.Equal("Requester One", detail.OwnerDisplayName);
        Assert.Equal("draft", detail.Status!.Key);
    }

    [Fact]
    public async Task The_staff_detail_offers_what_the_staff_member_may_do_and_the_reason_rules()
    {
        var reference = await _harness.InReviewAsync();
        _harness.SignIn(_harness.Staff);

        var detail = await DetailAsync(reference);

        Assert.Equal(WorkflowAudience.Staff, detail.Audience);
        Assert.Equal([TestRecordActions.Return, TestRecordActions.Accept, TestRecordActions.Decline], detail.AllowedActions.Select(action => action.ActionKey));
        var returnAction = detail.AllowedActions[0];
        Assert.Equal(ReasonRequirement.Required, returnAction.Reason);
        Assert.Equal(TestRecordWorkflow.ReasonField, returnAction.ReasonFieldKey);
        Assert.True(detail.AllowedActions.Single(action => action.ActionKey == TestRecordActions.Accept).ToTerminal);
    }

    [Fact]
    public async Task A_staff_member_without_the_decide_permission_is_offered_nothing_in_review()
    {
        var reference = await _harness.InReviewAsync();
        _harness.Staff.Grants.Remove(TestRecordPermissionKeys.Decide);
        _harness.SignIn(_harness.Staff);

        Assert.Empty((await DetailAsync(reference)).AllowedActions);
    }

    [Fact]
    public async Task Oversight_and_a_terminal_record_offer_no_actions()
    {
        var reference = await _harness.InReviewAsync();
        _harness.SignIn(_harness.Admin);
        Assert.Empty((await DetailAsync(reference)).AllowedActions);

        await _harness.AsAsync(_harness.Staff, reference, TestRecordActions.Decline, "Out of scope");
        foreach (var user in new[] { _harness.Requester, _harness.Staff })
        {
            _harness.SignIn(user);
            var detail = await DetailAsync(reference);
            Assert.Empty(detail.AllowedActions);
            Assert.True(detail.Status!.IsTerminal);
        }
    }

    [Fact]
    public async Task The_detail_carries_the_last_reason_and_the_enrichment()
    {
        var reference = await _harness.InReviewAsync();
        await _harness.AsAsync(_harness.Staff, reference, TestRecordActions.Return, "Page two is missing");
        var assignedAt = _harness.Clock.GetUtcNow();
        _harness.Assignments.Current[reference.Id] = new AssignmentSnapshot(_harness.Staff.UserId, "Staff One", assignedAt);
        _harness.Sla.Badges[reference.Id] = new SlaBadge(SlaState.DueSoon, assignedAt.AddDays(1));
        _harness.SignIn(_harness.Requester);

        var detail = await DetailAsync(reference);

        Assert.Equal("Page two is missing", detail.LastReason);
        Assert.Equal(_harness.Staff.UserId, detail.Enrichment.Assignee!.AssigneeUserId);
        Assert.Equal(SlaState.DueSoon, detail.Enrichment.Sla!.State);
        Assert.Equal([TestRecordActions.Resubmit], detail.AllowedActions.Select(action => action.ActionKey));
    }

    [Fact]
    public async Task Without_enrichment_readers_the_detail_has_no_assignee_or_badge()
    {
        var reference = await _harness.DraftAsync();
        var details = new GetRecordDetail(_harness.Loader, _harness.Store, _harness.Names, new RecordEnrichment([], []));

        var detail = (await details.ExecuteAsync(reference.RecordType, reference.Id, CancellationToken.None)).Value;

        Assert.Null(detail.Enrichment.Assignee);
        Assert.Null(detail.Enrichment.Sla);
    }

    [Fact]
    public async Task A_hidden_record_detail_is_404()
    {
        var reference = await _harness.DraftAsync();
        _harness.SignIn(_harness.Staff);

        Assert.Equal(RecordsErrorCodes.NotFound, (await _harness.Details.ExecuteAsync(reference.RecordType, reference.Id, CancellationToken.None)).Refusal!.Code);
    }

    [Fact]
    public async Task The_timeline_lists_every_entry_oldest_first_with_status_keys()
    {
        var reference = await _harness.InReviewAsync();
        _harness.SignIn(_harness.Staff);

        var timeline = (await new GetRecordTimeline(_harness.Loader, _harness.Store).ExecuteAsync(reference.RecordType, reference.Id, CancellationToken.None)).Value;

        Assert.Equal(["draft", "submitted", "in-review"], timeline.Select(entry => entry.ToStatusKey));
        Assert.Null(timeline[0].Entry.FromStatus);
    }

    [Fact]
    public async Task A_hidden_timeline_is_404()
    {
        var reference = await _harness.DraftAsync();
        _harness.SignIn(_harness.OtherStaff);

        Assert.False((await new GetRecordTimeline(_harness.Loader, _harness.Store).ExecuteAsync(reference.RecordType, reference.Id, CancellationToken.None)).IsSuccess);
    }

    [Fact]
    public async Task My_records_lists_only_the_callers_own_records_drafts_included_and_filters_by_status()
    {
        await _harness.SubmittedAsync();
        await _harness.DraftAsync();
        var other = _harness.Users.Add(userId: 60, "Requester Two", RecordsHarness.OwnOrganization)
            .Holds(TestRecordPermissionKeys.Create).Holds(LifecycleTestRecord.ViewOwn);
        _harness.SignIn(other);
        await _harness.Lifecycle.CreateDraftAsync(TestRecordWorkflow.RecordType, CancellationToken.None);
        _harness.SignIn(_harness.Requester);
        var list = new ListMyRecords(_harness.Types, new RecordCallers(_harness.Users, _harness.Users), _harness.Standings, _harness.Store);

        var all = (await list.ExecuteAsync(new MyRecordsQuery(TestRecordWorkflow.RecordType, Status: null, Page: null, PageSize: null), CancellationToken.None)).Value;
        var drafts = (await list.ExecuteAsync(new MyRecordsQuery(TestRecordWorkflow.RecordType, TestRecordStatuses.Draft, Page: 1, PageSize: 10), CancellationToken.None)).Value;

        Assert.Equal(2, all.Total);
        Assert.Equal(RecordsLimits.DefaultPageSize, all.PageSize);
        Assert.All(all.Items, item => Assert.Equal(_harness.Requester.UserId, item.Record.OwnerUserId));
        Assert.Equal("draft", Assert.Single(drafts.Items).StatusKey);
        Assert.Null(drafts.Items[0].OwnerDisplayName);
    }

    [Fact]
    public async Task My_records_needs_the_view_own_permission_a_known_type_and_an_account()
    {
        var list = new ListMyRecords(_harness.Types, new RecordCallers(_harness.Users, _harness.Users), _harness.Standings, _harness.Store);
        var query = new MyRecordsQuery(TestRecordWorkflow.RecordType, Status: null, Page: null, PageSize: null);

        _harness.SignIn(_harness.Staff);
        Assert.Equal(RecordsModuleErrorCodes.ViewForbidden, (await list.ExecuteAsync(query, CancellationToken.None)).Refusal!.Code);
        _harness.SignIn(null);
        Assert.Equal(RecordsModuleErrorCodes.ActorUnknown, (await list.ExecuteAsync(query, CancellationToken.None)).Refusal!.Code);
        Assert.Equal(RecordsErrorCodes.NotFound, (await list.ExecuteAsync(query with { RecordType = "other-record" }, CancellationToken.None)).Refusal!.Code);
    }

    [Fact]
    public async Task Oversight_lists_submitted_records_with_owners_and_filters_by_number()
    {
        await _harness.SubmittedAsync();
        await _harness.SubmittedAsync();
        await _harness.DraftAsync();
        _harness.SignIn(_harness.Admin);

        var all = await OversightAsync(new OversightQuery(TestRecordWorkflow.RecordType, Status: null, Number: null, Page: null, PageSize: null));
        var one = await OversightAsync(new OversightQuery(TestRecordWorkflow.RecordType, Status: null, Number: " 002 ", Page: null, PageSize: null));

        Assert.Equal(2, all.Value.Total);
        Assert.All(all.Value.Items, item => Assert.Equal("Requester One", item.OwnerDisplayName));
        Assert.Equal("TR-2026-002", Assert.Single(one.Value.Items).Record.Number);
    }

    [Fact]
    public async Task Own_organization_oversight_sees_only_its_organization()
    {
        await _harness.SubmittedAsync();
        var regional = _harness.Users.Add(userId: 70, "Regional Admin", RecordsHarness.OtherOrganization)
            .Holds(RecordsPermissionKeys.OversightView, PermissionScope.OwnOrganization);
        var unattached = _harness.Users.Add(userId: 71, "Unattached Admin", organizationId: null)
            .Holds(RecordsPermissionKeys.OversightView, PermissionScope.OwnOrganization);
        var query = new OversightQuery(TestRecordWorkflow.RecordType, Status: null, Number: null, Page: null, PageSize: null);

        _harness.SignIn(regional);
        Assert.Equal(0, (await OversightAsync(query)).Value.Total);
        _harness.SignIn(unattached);
        Assert.Empty((await OversightAsync(query)).Value.Items);
    }

    [Fact]
    public async Task Oversight_refuses_an_unknown_type_and_a_number_filter_longer_than_a_number()
    {
        _harness.SignIn(_harness.Admin);

        Assert.Equal(RecordsErrorCodes.NotFound, (await OversightAsync(new OversightQuery("other-record", null, null, null, null))).Refusal!.Code);
        var invalid = await OversightAsync(new OversightQuery(TestRecordWorkflow.RecordType, null, new string('1', 33), null, null));
        Assert.Equal(PlatformErrorCodes.Validation, invalid.Refusal!.Code);
        Assert.True(invalid.Refusal.FieldErrors.ContainsKey("number"));
    }

    private async Task<RecordDetailView> DetailAsync(Starter.Domain.Records.RecordRef reference) =>
        (await _harness.Details.ExecuteAsync(reference.RecordType, reference.Id, CancellationToken.None)).Value;

    private Task<Outcome<RecordListPage>> OversightAsync(OversightQuery query) =>
        new ListRecordOversight(_harness.Types, _harness.Standings, _harness.Store, _harness.Names, new OversightQueryValidator())
            .ExecuteAsync(query, CancellationToken.None);
}
