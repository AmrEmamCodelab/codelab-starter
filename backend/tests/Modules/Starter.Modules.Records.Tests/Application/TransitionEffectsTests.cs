using Starter.Application.Notifications;
using Starter.Application.Records;
using Starter.Modules.Records.Application;
using Starter.Modules.Records.Tests.Support;
using Starter.Workflow.Testing;

namespace Starter.Modules.Records.Tests.Application;

public sealed class TransitionEffectsTests
{
    private readonly RecordsHarness _harness = new();

    [Fact]
    public async Task Every_observer_sees_the_transition_after_it_is_saved()
    {
        var reference = await _harness.SubmittedAsync();

        var seen = Assert.Single(_harness.Observer.Seen);
        Assert.Equal(reference.Id, seen.RecordId);
        Assert.Equal(TestRecordWorkflow.RecordType, seen.RecordType);
        Assert.Equal((TestRecordStatuses.Draft, TestRecordStatuses.Submitted), (seen.FromStatus, seen.ToStatus));
        Assert.Equal(TestRecordActions.Submit, seen.ActionKey);
        Assert.Equal(_harness.Requester.UserId, seen.ActorUserId);
        Assert.False(seen.ToTerminal);
        Assert.Equal(_harness.Clock.GetUtcNow(), seen.At);
    }

    [Fact]
    public async Task A_terminal_transition_is_flagged_for_observers()
    {
        var reference = await _harness.InReviewAsync();

        await _harness.AsAsync(_harness.Staff, reference, TestRecordActions.Accept);

        Assert.True(_harness.Observer.Seen[^1].ToTerminal);
    }

    [Fact]
    public async Task A_failing_observer_never_undoes_or_fails_the_transition()
    {
        var reference = await _harness.DraftAsync();
        _harness.Observer.Fail = true;

        Assert.IsType<SubmitResult.Submitted>(await _harness.Lifecycle.SubmitAsync(reference, CancellationToken.None));
        Assert.Equal(TestRecordStatuses.Submitted, _harness.RecordOf(reference).Status);
    }

    [Fact]
    public async Task The_requester_is_notified_with_the_rules_template_and_placeholders()
    {
        var reference = await _harness.InReviewAsync();

        await _harness.AsAsync(_harness.Staff, reference, TestRecordActions.Return, "Page two is missing");

        var sent = Assert.Single(_harness.Notifications.Sent);
        Assert.Equal(_harness.Requester.UserId, sent.UserId);
        Assert.Equal("test-records.returned", sent.TemplateKey);
        Assert.Equal(NotificationChannels.InApp | NotificationChannels.Email, sent.Channels);
        Assert.Equal("TR-2026-001", sent.Placeholders[RecordNotificationPlaceholders.RecordNumber]);
        Assert.Equal("returned", sent.Placeholders[RecordNotificationPlaceholders.StatusKey]);
        Assert.Equal(TestRecordActions.Return, sent.Placeholders[RecordNotificationPlaceholders.ActionKey]);
        Assert.Equal("Page two is missing", sent.Placeholders[RecordNotificationPlaceholders.Reason]);
        Assert.Null(sent.Link);
    }

    [Fact]
    public async Task The_assignee_is_notified_when_the_rule_asks_and_someone_holds_the_record()
    {
        var reference = await _harness.InReviewAsync();
        await _harness.AsAsync(_harness.Staff, reference, TestRecordActions.Return, "Fix it");
        _harness.Assignments.Current[reference.Id] = new AssignmentSnapshot(_harness.Staff.UserId, "Staff One", _harness.Clock.GetUtcNow());
        _harness.Notifications.Sent.Clear();
        _harness.SignIn(_harness.Requester);

        await _harness.Lifecycle.ResubmitAsync(reference, CancellationToken.None);

        var sent = Assert.Single(_harness.Notifications.Sent);
        Assert.Equal(_harness.Staff.UserId, sent.UserId);
        Assert.Equal("test-records.submitted", sent.TemplateKey);
    }

    [Fact]
    public async Task Nobody_is_notified_when_the_decision_says_none_or_the_assignee_is_missing()
    {
        await _harness.SubmittedAsync();
        var draft = await _harness.DraftAsync();
        await _harness.Lifecycle.CancelAsync(draft, reason: null, CancellationToken.None);

        Assert.Empty(_harness.Notifications.Sent);
    }

    [Fact]
    public async Task A_failing_notification_never_fails_the_committed_transition()
    {
        var reference = await _harness.InReviewAsync();
        _harness.Notifications.Fail = true;

        Assert.Equal(TestRecordStatuses.Returned, await _harness.AsAsync(_harness.Staff, reference, TestRecordActions.Return, "Fix it"));
    }

    [Fact]
    public async Task Placeholders_leave_out_a_missing_number_and_reason()
    {
        var reference = await _harness.DraftAsync();
        var loaded = (await _harness.Loader.LoadAsync(reference, CancellationToken.None)).Value;
        var rule = loaded.Type.Catalog.RulesFrom(TestRecordStatuses.Draft).First(candidate => candidate.ActionKey == RecordLifecycleActions.Cancel);

        var values = TransitionEffects.PlaceholdersOf(new CommittedTransition(
            loaded.Record, rule, TestRecordStatuses.Draft, Reason: null, loaded.Standing.Caller, loaded.Type, _harness.Clock.GetUtcNow()));

        Assert.False(values.ContainsKey(RecordNotificationPlaceholders.RecordNumber));
        Assert.False(values.ContainsKey(RecordNotificationPlaceholders.Reason));
        Assert.Equal("withdrawn", values[RecordNotificationPlaceholders.StatusKey]);
    }
}
