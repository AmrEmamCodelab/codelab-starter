using Starter.Application.Attachments;
using Starter.Application.Records;
using Starter.Application.Results;
using Starter.Application.Workflow;
using Starter.Modules.Records.Application;
using Starter.Modules.Records.Tests.Support;
using Starter.Workflow.Testing;

namespace Starter.Modules.Records.Tests.Application;

public sealed class RecordPortsTests
{
    private readonly RecordsHarness _harness = new();

    [Fact]
    public async Task The_owner_may_change_files_while_the_record_is_a_draft_or_returned()
    {
        var reference = await _harness.InReviewAsync();
        var access = Access();
        _harness.SignIn(_harness.Requester);
        Assert.True(await access.CanViewAsync(reference.Id, CancellationToken.None));
        Assert.False(await access.CanChangeAsync(reference.Id, CancellationToken.None));

        await _harness.AsAsync(_harness.Staff, reference, TestRecordActions.Return, "Add page two");
        _harness.SignIn(_harness.Requester);
        Assert.True(await access.CanChangeAsync(reference.Id, CancellationToken.None));

        var draft = await _harness.DraftAsync();
        Assert.True(await access.CanChangeAsync(draft.Id, CancellationToken.None));
    }

    [Fact]
    public async Task Staff_may_view_but_never_change_and_strangers_may_do_neither()
    {
        var reference = await _harness.SubmittedAsync();
        var access = Access();

        _harness.SignIn(_harness.Staff);
        Assert.True(await access.CanViewAsync(reference.Id, CancellationToken.None));
        Assert.False(await access.CanChangeAsync(reference.Id, CancellationToken.None));
        _harness.SignIn(_harness.OtherStaff);
        Assert.False(await access.CanViewAsync(reference.Id, CancellationToken.None));
        Assert.False(await access.CanViewAsync(recordId: 999, CancellationToken.None));
    }

    [Fact]
    public void One_access_per_record_type_keyed_by_its_attachable_type()
    {
        var accesses = new RecordAttachmentAccesses(_harness.Types, _harness.Loader);

        Assert.Equal(LifecycleTestRecord.AttachableRecordType, Assert.Single(accesses.All).RecordType);
        Assert.NotNull(accesses.For(LifecycleTestRecord.AttachableRecordType));
        Assert.Null(accesses.For("other-files"));
    }

    [Fact]
    public void The_accesses_are_published_as_the_kernel_access_source_without_host_wiring()
    {
        var accesses = new RecordAttachmentAccesses(_harness.Types, _harness.Loader);

        Assert.IsAssignableFrom<IAttachableRecordAccessSource>(accesses);
        Assert.Equal([LifecycleTestRecord.AttachableRecordType], accesses.Accesses.Select(access => access.RecordType));
    }

    [Fact]
    public async Task The_directory_answers_summaries_and_leaves_drafts_out_of_queries()
    {
        var submitted = await _harness.SubmittedAsync();
        var draft = await _harness.DraftAsync();
        var directory = new RecordDirectory(_harness.Store, _harness.Types);

        var summaries = await directory.GetAsync([submitted.Id, draft.Id, 999], CancellationToken.None);
        var page = await directory.QueryAsync(
            new RecordQuery(TestRecordWorkflow.RecordType, Statuses: null, NumberContains: null, OrganizationId: null, SubmittedFrom: null, SubmittedTo: null, Ids: null, Page: 1, PageSize: 10),
            CancellationToken.None);

        Assert.Equal(2, summaries.Count);
        var summary = summaries.Single(item => item.Id == submitted.Id);
        Assert.Equal(("TR-2026-001", TestRecordStatuses.Submitted, false), (summary.Number, summary.Status, summary.IsTerminal));
        Assert.Equal(RecordsHarness.OwnOrganization, summary.OrganizationId);
        Assert.Equal(submitted.Id, Assert.Single(page.Items).Id);
        Assert.Empty(await directory.GetAsync([], CancellationToken.None));
    }

    [Fact]
    public async Task The_directory_filters_by_status_ids_organization_and_submit_window()
    {
        var first = await _harness.SubmittedAsync();
        var second = await _harness.InReviewAsync();
        var submittedAt = _harness.RecordOf(second).SubmittedAt!.Value;
        var directory = new RecordDirectory(_harness.Store, _harness.Types);

        var inReview = await directory.QueryAsync(Query(statuses: [TestRecordStatuses.InReview]), CancellationToken.None);
        var byId = await directory.QueryAsync(Query(ids: [first.Id]), CancellationToken.None);
        var otherOrganization = await directory.QueryAsync(Query(organizationId: RecordsHarness.OtherOrganization), CancellationToken.None);
        var window = await directory.QueryAsync(Query(from: submittedAt, to: submittedAt), CancellationToken.None);

        Assert.Equal(second.Id, Assert.Single(inReview.Items).Id);
        Assert.Equal(first.Id, Assert.Single(byId.Items).Id);
        Assert.Empty(otherOrganization.Items);
        Assert.Equal(second.Id, Assert.Single(window.Items).Id);
    }

    [Fact]
    public void A_transition_command_needs_an_action_that_fits_the_timeline()
    {
        var validator = new TransitionCommandValidator();

        Assert.True(validator.Validate(new TransitionCommand("take", Reason: null)).IsValid);
        Assert.False(validator.Validate(new TransitionCommand(" ", Reason: null)).IsValid);
        Assert.True(validator.Validate(new TransitionCommand(new string('a', 65), Reason: null)).ToDictionary().ContainsKey("action"));
    }

    [Fact]
    public async Task Taking_a_transition_answers_the_new_detail()
    {
        var reference = await _harness.SubmittedAsync();
        _harness.SignIn(_harness.Staff);
        var take = new TakeRecordTransition(new TransitionCommandValidator(), _harness.Transitions, _harness.Loader, _harness.Details);

        var detail = (await take.ExecuteAsync(reference.RecordType, reference.Id, new TransitionCommand(TestRecordActions.Take, Reason: null), CancellationToken.None)).Value;

        Assert.Equal(TestRecordStatuses.InReview, detail.Record.Status);
        Assert.Contains(detail.AllowedActions, action => action.ActionKey == TestRecordActions.Accept);
    }

    [Fact]
    public async Task Taking_a_transition_refuses_an_invalid_body_and_passes_workflow_refusals_through()
    {
        var reference = await _harness.InReviewAsync();
        _harness.SignIn(_harness.Staff);
        var take = new TakeRecordTransition(new TransitionCommandValidator(), _harness.Transitions, _harness.Loader, _harness.Details);

        var invalid = await take.ExecuteAsync(reference.RecordType, reference.Id, new TransitionCommand(Action: null, Reason: null), CancellationToken.None);
        var noReason = await take.ExecuteAsync(reference.RecordType, reference.Id, new TransitionCommand(TestRecordActions.Return, Reason: null), CancellationToken.None);

        Assert.Equal(PlatformErrorCodes.Validation, invalid.Refusal!.Code);
        Assert.Equal(WorkflowErrorCodes.ReasonRequired, noReason.Refusal!.Code);
    }

    [Fact]
    public void The_lifecycle_catalog_passes_the_kit_fixture_end_to_end()
    {
        TransitionFixture.For(LifecycleTestRecord.Catalog)
            .From(TestRecordStatuses.Draft).WithPermissions(TestRecordPermissionKeys.Create).AsOwner()
            .ExpectAllowedActions(TestRecordActions.Submit, RecordLifecycleActions.Cancel)
            .Do(TestRecordActions.Submit).ExpectStatus(TestRecordStatuses.Submitted);
        TransitionFixture.For(LifecycleTestRecord.Catalog)
            .From(TestRecordStatuses.InReview).WithPermissions(TestRecordPermissionKeys.Decide).AsNonOwner()
            .Do(TestRecordActions.Return).ExpectRefused(WorkflowRefusal.ReasonRequired);
        TransitionFixture.For(LifecycleTestRecord.Catalog)
            .From(TestRecordStatuses.InReview).WithPermissions(TestRecordPermissionKeys.Decide).AsNonOwner()
            .Do(TestRecordActions.Accept).ExpectTerminal();
    }

    private RecordAttachmentAccess Access() => new(TestRecordWorkflow.RecordType, LifecycleTestRecord.AttachableRecordType, _harness.Loader);

    private static RecordQuery Query(
        IReadOnlyCollection<int>? statuses = null,
        IReadOnlyCollection<long>? ids = null,
        long? organizationId = null,
        DateTimeOffset? from = null,
        DateTimeOffset? to = null) =>
        new(TestRecordWorkflow.RecordType, statuses, NumberContains: null, organizationId, SubmittedFrom: from, SubmittedTo: to, ids, Page: 1, PageSize: 10);
}
