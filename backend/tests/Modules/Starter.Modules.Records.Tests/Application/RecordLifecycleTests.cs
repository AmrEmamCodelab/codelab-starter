using Starter.Application.Attachments;
using Starter.Application.Authorization;
using Starter.Application.Records;
using Starter.Application.Results;
using Starter.Application.Workflow;
using Starter.Domain.Exceptions;
using Starter.Domain.Records;
using Starter.Domain.Workflow;
using Starter.Modules.Records.Application;
using Starter.Modules.Records.Tests.Support;
using Starter.Workflow.Testing;

namespace Starter.Modules.Records.Tests.Application;

public sealed class RecordLifecycleTests
{
    private readonly RecordsHarness _harness = new();

    [Fact]
    public async Task A_draft_belongs_to_the_caller_and_its_organization_and_opens_the_timeline()
    {
        var reference = await _harness.DraftAsync();

        var record = _harness.RecordOf(reference);
        Assert.Equal(TestRecordWorkflow.RecordType, reference.RecordType);
        Assert.Equal(TestRecordStatuses.Draft, record.Status);
        Assert.Equal(_harness.Requester.UserId, record.OwnerUserId);
        Assert.Equal(RecordsHarness.OwnOrganization, record.OrganizationId);
        var first = Assert.Single(_harness.Store.History);
        Assert.Equal(RecordLifecycleActions.Create, first.Action);
        Assert.Null(first.FromStatus);
        Assert.Equal(TestRecordStatuses.Draft, first.ToStatus);
        Assert.Equal("Requester One", first.ActorName);
        Assert.Equal(TransitionSide.Requester, first.Side);
    }

    [Fact]
    public async Task Creating_a_draft_needs_an_account_and_the_create_permission()
    {
        _harness.SignIn(null);
        await Assert.ThrowsAsync<AccessDeniedException>(() => _harness.Lifecycle.CreateDraftAsync(TestRecordWorkflow.RecordType, CancellationToken.None));

        _harness.SignIn(_harness.Staff);
        await Assert.ThrowsAsync<AccessDeniedException>(() => _harness.Lifecycle.CreateDraftAsync(TestRecordWorkflow.RecordType, CancellationToken.None));
    }

    [Fact]
    public async Task Creating_a_draft_of_an_unregistered_type_is_a_wiring_fault()
    {
        await Assert.ThrowsAsync<ArgumentException>(() => _harness.Lifecycle.CreateDraftAsync("other-record", CancellationToken.None));
    }

    [Fact]
    public async Task Submit_takes_the_prefix_number_moves_the_record_and_writes_history()
    {
        var reference = await _harness.DraftAsync();

        var result = await _harness.Lifecycle.SubmitAsync(reference, CancellationToken.None);

        Assert.Equal("TR-2026-001", Assert.IsType<SubmitResult.Submitted>(result).Number);
        var record = _harness.RecordOf(reference);
        Assert.Equal(TestRecordStatuses.Submitted, record.Status);
        Assert.Equal("TR-2026-001", record.Number);
        Assert.Equal(_harness.Clock.GetUtcNow(), record.SubmittedAt);
        var entry = _harness.Store.History[^1];
        Assert.Equal((TestRecordActions.Submit, TestRecordStatuses.Draft, TestRecordStatuses.Submitted), (entry.Action, entry.FromStatus!.Value, entry.ToStatus!.Value));
    }

    [Fact]
    public async Task Numbers_count_up_per_prefix()
    {
        await _harness.SubmittedAsync();
        var second = await _harness.DraftAsync();

        Assert.Equal("TR-2026-002", Assert.IsType<SubmitResult.Submitted>(await _harness.Lifecycle.SubmitAsync(second, CancellationToken.None)).Number);
    }

    [Fact]
    public async Task Submit_asks_the_interlock_with_the_attachable_record_type()
    {
        var reference = await _harness.DraftAsync();

        await _harness.Lifecycle.SubmitAsync(reference, CancellationToken.None);

        Assert.Equal((LifecycleTestRecord.AttachableRecordType, reference.Id), Assert.Single(_harness.Interlock.Asked));
    }

    [Fact]
    public async Task A_failing_submit_validator_refuses_with_the_field_errors_and_burns_no_number()
    {
        var reference = await _harness.DraftAsync();
        _harness.Validator.Errors.Add(new FieldError("title", "REQUIRED"));

        var refused = Assert.IsType<SubmitResult.Refused>(await _harness.Lifecycle.SubmitAsync(reference, CancellationToken.None));

        Assert.Equal(SubmitRefusal.ValidationFailed, refused.Reason);
        Assert.Equal(["REQUIRED"], refused.ToRefusal().FieldErrors["title"]);
        Assert.Equal(0, _harness.Numbers.Taken);
        Assert.Equal(TestRecordStatuses.Draft, _harness.RecordOf(reference).Status);
        Assert.Empty(_harness.Interlock.Asked);
    }

    [Fact]
    public async Task A_missing_required_attachment_refuses_409_and_linking_it_lets_the_submit_through()
    {
        var reference = await _harness.DraftAsync();
        _harness.Interlock.Missing.Add(reference.Id);

        var refused = Assert.IsType<SubmitResult.Refused>(await _harness.Lifecycle.SubmitAsync(reference, CancellationToken.None));
        Assert.Equal(SubmitRefusal.AttachmentsMissing, refused.Reason);
        Assert.Equal(AttachmentErrorCodes.RequiredMissing, refused.ToRefusal().Code);
        Assert.Equal(0, _harness.Numbers.Taken);

        _harness.Interlock.Missing.Clear();
        Assert.IsType<SubmitResult.Submitted>(await _harness.Lifecycle.SubmitAsync(reference, CancellationToken.None));
    }

    [Fact]
    public async Task Submitting_twice_is_not_submittable_and_another_users_draft_is_not_found()
    {
        var reference = await _harness.SubmittedAsync();

        Assert.Equal(SubmitRefusal.NotSubmittable, Refusal(await _harness.Lifecycle.SubmitAsync(reference, CancellationToken.None)));

        var draft = await _harness.DraftAsync();
        _harness.SignIn(_harness.Staff);
        Assert.Equal(SubmitRefusal.NotFound, Refusal(await _harness.Lifecycle.SubmitAsync(draft, CancellationToken.None)));
        Assert.Equal(SubmitRefusal.NotFound, Refusal(await _harness.Lifecycle.SubmitAsync(new RecordRef(999, TestRecordWorkflow.RecordType), CancellationToken.None)));
    }

    [Fact]
    public async Task Staff_who_may_see_a_record_but_not_submit_it_get_not_owner()
    {
        var reference = await _harness.SubmittedAsync();
        _harness.SignIn(_harness.Staff);

        Assert.Equal(SubmitRefusal.NotOwner, Refusal(await _harness.Lifecycle.ResubmitAsync(reference, CancellationToken.None)));
    }

    [Fact]
    public async Task A_caller_without_an_account_is_refused_as_not_owner()
    {
        var reference = await _harness.DraftAsync();
        _harness.SignIn(null);

        Assert.Equal(SubmitRefusal.NotOwner, Refusal(await _harness.Lifecycle.SubmitAsync(reference, CancellationToken.None)));
    }

    [Fact]
    public async Task A_concurrent_change_refuses_the_submit_with_concurrency()
    {
        var reference = await _harness.DraftAsync();
        _harness.Store.LoseNextSave = true;

        Assert.Equal(SubmitRefusal.Concurrency, Refusal(await _harness.Lifecycle.SubmitAsync(reference, CancellationToken.None)));
        Assert.Single(_harness.Store.History);
    }

    [Fact]
    public async Task A_resubmit_after_a_return_keeps_the_number_and_reruns_the_checks()
    {
        var reference = await _harness.InReviewAsync();
        await _harness.AsAsync(_harness.Staff, reference, TestRecordActions.Return, "Page two is missing");
        _harness.SignIn(_harness.Requester);
        _harness.Interlock.Missing.Add(reference.Id);

        Assert.Equal(SubmitRefusal.AttachmentsMissing, Refusal(await _harness.Lifecycle.ResubmitAsync(reference, CancellationToken.None)));

        _harness.Interlock.Missing.Clear();
        Assert.Equal("TR-2026-001", Assert.IsType<SubmitResult.Submitted>(await _harness.Lifecycle.ResubmitAsync(reference, CancellationToken.None)).Number);
        Assert.Equal(1, _harness.Numbers.Taken);
        Assert.Equal(TestRecordStatuses.Submitted, _harness.RecordOf(reference).Status);
    }

    [Fact]
    public async Task The_generic_transition_routes_submit_through_the_submit_checks()
    {
        var reference = await _harness.DraftAsync();
        _harness.Interlock.Missing.Add(reference.Id);

        var refusal = await _harness.RefusedAsync(_harness.Requester, reference, TestRecordActions.Submit);

        Assert.Equal(AttachmentErrorCodes.RequiredMissing, refusal.Code);
        Assert.Equal(RefusalCategory.Conflict, refusal.Category);
    }

    [Fact]
    public async Task The_generic_transition_keeps_workflow_codes_for_a_submit_from_the_wrong_status()
    {
        var reference = await _harness.SubmittedAsync();

        Assert.Equal(WorkflowErrorCodes.ActionNotAllowed, (await _harness.RefusedAsync(_harness.Requester, reference, TestRecordActions.Submit)).Code);
    }

    [Fact]
    public async Task Cancel_takes_the_cancel_rule_with_an_optional_reason()
    {
        var reference = await _harness.DraftAsync();

        var result = await _harness.Lifecycle.CancelAsync(reference, "  No longer needed  ", CancellationToken.None);

        Assert.Equal(TestRecordStatuses.Withdrawn, Assert.IsType<TransitionResult.Transitioned>(result).NewStatus);
        Assert.Equal("No longer needed", _harness.Store.History[^1].Reason);
        Assert.Equal(RecordLifecycleActions.Cancel, _harness.Store.History[^1].Action);
    }

    [Fact]
    public async Task A_return_without_its_mandatory_reason_is_400_on_the_reason_field()
    {
        var reference = await _harness.InReviewAsync();

        var refusal = await _harness.RefusedAsync(_harness.Staff, reference, TestRecordActions.Return, reason: "   ");

        Assert.Equal(WorkflowErrorCodes.ReasonRequired, refusal.Code);
        Assert.Equal(RefusalCategory.Invalid, refusal.Category);
        Assert.True(refusal.FieldErrors.ContainsKey(TestRecordWorkflow.ReasonField));
        Assert.Equal(TestRecordStatuses.InReview, _harness.RecordOf(reference).Status);
    }

    [Fact]
    public async Task An_action_from_the_wrong_status_is_409_and_an_unknown_one_404()
    {
        var reference = await _harness.SubmittedAsync();

        Assert.Equal(WorkflowErrorCodes.ActionNotAllowed, (await _harness.RefusedAsync(_harness.Staff, reference, TestRecordActions.Accept)).Code);
        Assert.Equal(WorkflowErrorCodes.ActionUnknown, (await _harness.RefusedAsync(_harness.Staff, reference, "teleport")).Code);
    }

    [Fact]
    public async Task Staff_without_the_rules_permission_get_403_and_the_requester_cannot_act_as_staff()
    {
        var reference = await _harness.InReviewAsync();

        Assert.Equal(WorkflowErrorCodes.Forbidden, (await _harness.RefusedAsync(_harness.Requester, reference, TestRecordActions.Accept)).Code);
    }

    [Fact]
    public async Task A_requester_holding_every_staff_key_cannot_review_or_decide_their_own_record()
    {
        _harness.Requester
            .Holds(LifecycleTestRecord.Queue)
            .Holds(TestRecordPermissionKeys.Review)
            .Holds(TestRecordPermissionKeys.Decide);
        var reference = await _harness.SubmittedAsync();

        Assert.Equal(WorkflowErrorCodes.Forbidden, (await _harness.RefusedAsync(_harness.Requester, reference, TestRecordActions.Take)).Code);
        await _harness.AsAsync(_harness.Staff, reference, TestRecordActions.Take);
        Assert.Equal(WorkflowErrorCodes.Forbidden, (await _harness.RefusedAsync(_harness.Requester, reference, TestRecordActions.Accept)).Code);
        Assert.Equal(TestRecordStatuses.InReview, _harness.RecordOf(reference).Status);
    }

    [Fact]
    public async Task An_owner_without_view_own_holding_staff_keys_still_cannot_decide_their_own_record()
    {
        _harness.Requester
            .Holds(LifecycleTestRecord.Queue)
            .Holds(TestRecordPermissionKeys.Review)
            .Holds(TestRecordPermissionKeys.Decide);
        var reference = await _harness.SubmittedAsync();
        _harness.Requester.Grants.Remove(LifecycleTestRecord.ViewOwn);

        Assert.Equal(WorkflowErrorCodes.Forbidden, (await _harness.RefusedAsync(_harness.Requester, reference, TestRecordActions.Take)).Code);
        Assert.Equal(TestRecordStatuses.Submitted, _harness.RecordOf(reference).Status);
    }

    [Fact]
    public async Task An_owners_staff_grant_scoped_to_another_organization_does_not_count_on_their_own_record()
    {
        const long ForeignOrganization = 999;
        foreach (var key in new[] { LifecycleTestRecord.Queue, TestRecordPermissionKeys.Review, TestRecordPermissionKeys.Decide })
        {
            _harness.Requester.Grants[key] = new ScopeDecision(Allowed: true, PermissionScope.OwnOrganization, ForeignOrganization);
        }

        var reference = await _harness.SubmittedAsync();
        var standing = (await _harness.Loader.LoadAsync(reference, CancellationToken.None)).Value.Standing;

        Assert.True(standing.Holds(TestRecordPermissionKeys.Create));
        Assert.False(standing.Holds(TestRecordPermissionKeys.Review));
        Assert.False(standing.Holds(TestRecordPermissionKeys.Decide));
        Assert.Equal(WorkflowErrorCodes.Forbidden, (await _harness.RefusedAsync(_harness.Requester, reference, TestRecordActions.Take)).Code);
    }

    [Fact]
    public async Task Staff_of_another_organization_do_not_see_the_record()
    {
        var reference = await _harness.SubmittedAsync();

        Assert.Equal(RecordsErrorCodes.NotFound, (await _harness.RefusedAsync(_harness.OtherStaff, reference, TestRecordActions.Take)).Code);
    }

    [Fact]
    public async Task A_terminal_record_refuses_every_further_action()
    {
        var reference = await _harness.InReviewAsync();
        await _harness.AsAsync(_harness.Staff, reference, TestRecordActions.Accept);

        Assert.Equal(WorkflowErrorCodes.ActionNotAllowed, (await _harness.RefusedAsync(_harness.Staff, reference, TestRecordActions.Decline, "x")).Code);
        Assert.Equal(WorkflowErrorCodes.ActionNotAllowed, (await _harness.RefusedAsync(_harness.Requester, reference, RecordLifecycleActions.Cancel)).Code);
    }

    [Fact]
    public async Task A_concurrent_change_refuses_a_transition_with_409_concurrency()
    {
        var reference = await _harness.SubmittedAsync();
        _harness.Store.LoseNextSave = true;

        var refusal = await _harness.RefusedAsync(_harness.Staff, reference, TestRecordActions.Take);

        Assert.Equal(RecordsErrorCodes.Concurrency, refusal.Code);
        Assert.Equal(RefusalCategory.Conflict, refusal.Category);
    }

    [Fact]
    public async Task Every_step_of_a_full_flow_is_on_the_timeline_with_its_actor_and_side()
    {
        var reference = await _harness.InReviewAsync();
        await _harness.AsAsync(_harness.Staff, reference, TestRecordActions.Return, "Fix it");
        _harness.SignIn(_harness.Requester);
        await _harness.Lifecycle.ResubmitAsync(reference, CancellationToken.None);
        await _harness.AsAsync(_harness.Staff, reference, TestRecordActions.Take);
        await _harness.AsAsync(_harness.Staff, reference, TestRecordActions.Accept);

        var actions = _harness.Store.History.Select(entry => (entry.Action, entry.ActorName, entry.Side)).ToList();
        Assert.Equal(
            [
                (RecordLifecycleActions.Create, "Requester One", TransitionSide.Requester),
                (TestRecordActions.Submit, "Requester One", TransitionSide.Requester),
                (TestRecordActions.Take, "Staff One", TransitionSide.Staff),
                (TestRecordActions.Return, "Staff One", TransitionSide.Staff),
                (TestRecordActions.Resubmit, "Requester One", TransitionSide.Requester),
                (TestRecordActions.Take, "Staff One", TransitionSide.Staff),
                (TestRecordActions.Accept, "Staff One", TransitionSide.Staff),
            ],
            actions);
    }

    private static SubmitRefusal Refusal(SubmitResult result) => Assert.IsType<SubmitResult.Refused>(result).Reason;
}
