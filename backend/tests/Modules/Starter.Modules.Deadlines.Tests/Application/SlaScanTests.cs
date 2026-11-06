using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using Starter.Application.Jobs;
using Starter.Modules.Deadlines.Application;
using Starter.Modules.Deadlines.Domain;
using Starter.Modules.Deadlines.Tests.Fakes;

namespace Starter.Modules.Deadlines.Tests.Application;

public sealed class SlaScanTests
{
    private const long Assignee = 41;
    private const long OtherAssignee = 42;
    private const long QueueStaffMember = 51;

    private readonly InMemoryDeadlineStore _store = new();
    private readonly FakeTimeProvider _clock = new(TestRecord.Start);
    private readonly FakeNotifications _notifications = new();
    private readonly FakeRecordDirectory _directory = new();
    private readonly FakeAssignments _assignments = new();
    private readonly FakeHolders _holders = new();
    private SlaPolicyCatalog _policies = new([new PolicySource(TestRecord.ReviewDefinition())]);
    private bool _withoutDirectory;

    [Fact]
    public async Task Nothing_is_breached_before_the_deadline()
    {
        UnderReview(7, assignee: Assignee);
        _clock.Advance(TestRecord.ReviewSla - TimeSpan.FromTicks(1));

        var result = await Scan().RunAsync(CancellationToken.None);

        Assert.Equal(0, result.Breached);
        Assert.Empty(_notifications.Sent);
        Assert.Null(Assert.Single(_store.Saved).BreachedAt);
    }

    [Fact]
    public async Task A_breach_exactly_at_the_deadline_is_marked_and_escalated_once_to_the_assignee()
    {
        UnderReview(7, assignee: Assignee);
        _clock.Advance(TestRecord.ReviewSla);

        var result = await Scan().RunAsync(CancellationToken.None);

        Assert.Equal(new SlaScanResult(Breached: 1, Escalated: 1, Unaddressed: 0, Skipped: 0, Closed: 0), result);
        var deadline = Assert.Single(_store.Saved);
        Assert.Equal(_clock.GetUtcNow(), deadline.BreachedAt);
        Assert.Equal(_clock.GetUtcNow(), deadline.EscalatedAt);
        var sent = Assert.Single(_notifications.Sent);
        Assert.Equal(Assignee, sent.UserId);
        Assert.Equal(TestRecord.EscalationTemplate, sent.TemplateKey);
        Assert.Equal(DeadlinesNotifications.EscalationChannels, sent.Channels);
    }

    [Fact]
    public async Task The_escalation_carries_the_number_the_status_key_and_the_deadline()
    {
        UnderReview(7, assignee: Assignee);
        _clock.Advance(TestRecord.ReviewSla + TimeSpan.FromMinutes(3));

        await Scan().RunAsync(CancellationToken.None);

        var placeholders = Assert.Single(_notifications.Sent).Placeholders;
        Assert.Equal("TR-2026-007", placeholders[DeadlinesNotifications.RecordNumber]);
        Assert.Equal(TestRecord.UnderReviewKey, placeholders[DeadlinesNotifications.StatusKey]);
        Assert.Equal("2026-11-09 09:00 UTC", placeholders[DeadlinesNotifications.DueAt]);
    }

    [Fact]
    public async Task A_re_run_sends_nothing_again()
    {
        UnderReview(7, assignee: Assignee);
        _clock.Advance(TestRecord.ReviewSla);
        await Scan().RunAsync(CancellationToken.None);
        _clock.Advance(TimeSpan.FromMinutes(5));

        var second = await Scan().RunAsync(CancellationToken.None);

        Assert.Equal(new SlaScanResult(Breached: 0, Escalated: 0, Unaddressed: 0, Skipped: 0, Closed: 0), second);
        Assert.Single(_notifications.Sent);
    }

    [Fact]
    public async Task An_unassigned_record_escalates_to_every_queue_staff_member()
    {
        UnderReview(7);
        _holders.Holders[(TestRecord.QueuePermission, null)] = [QueueStaffMember, OtherAssignee];
        _clock.Advance(TestRecord.ReviewSla);

        await Scan().RunAsync(CancellationToken.None);

        Assert.Equal([OtherAssignee, QueueStaffMember], _notifications.Sent.Select(sent => sent.UserId));
    }

    [Fact]
    public async Task A_breach_with_nobody_to_notify_is_claimed_and_counted()
    {
        UnderReview(7);
        _clock.Advance(TestRecord.ReviewSla);

        var result = await Scan().RunAsync(CancellationToken.None);

        Assert.Equal(1, result.Unaddressed);
        Assert.NotNull(Assert.Single(_store.Saved).EscalatedAt);
        Assert.Empty(_notifications.Sent);
    }

    [Fact]
    public async Task A_failure_before_any_recipient_gives_the_breach_back_and_the_next_run_retries_it()
    {
        UnderReview(7, assignee: Assignee);
        UnderReview(8, assignee: OtherAssignee);
        _notifications.FailFor.Add(Assignee);
        _clock.Advance(TestRecord.ReviewSla);

        await Assert.ThrowsAsync<InvalidOperationException>(() => Scan().RunAsync(CancellationToken.None));

        Assert.Equal([OtherAssignee], _notifications.Sent.Select(sent => sent.UserId)); // the other breach still went out
        Assert.Null(Deadline(7).EscalatedAt);
        _notifications.FailFor.Clear();
        var retry = await Scan().RunAsync(CancellationToken.None);
        Assert.Equal(1, retry.Escalated);
        Assert.Equal([OtherAssignee, Assignee], _notifications.Sent.Select(sent => sent.UserId));
    }

    [Fact]
    public async Task A_failure_after_some_recipients_keeps_the_claim_so_nobody_is_notified_twice()
    {
        UnderReview(7);
        _holders.Holders[(TestRecord.QueuePermission, null)] = [Assignee, OtherAssignee];
        _notifications.FailFor.Add(OtherAssignee);
        _clock.Advance(TestRecord.ReviewSla);

        await Assert.ThrowsAsync<InvalidOperationException>(() => Scan().RunAsync(CancellationToken.None));
        _notifications.FailFor.Clear();
        await Scan().RunAsync(CancellationToken.None);

        Assert.Equal([Assignee], _notifications.Sent.Select(sent => sent.UserId));
        Assert.NotNull(Deadline(7).EscalatedAt);
    }

    [Theory]
    [InlineData(TestRecord.Returned, false)]
    [InlineData(TestRecord.Closed, true)]
    public async Task A_deadline_whose_record_left_the_status_is_closed_instead_of_escalated(int status, bool terminal)
    {
        UnderReview(7, assignee: Assignee);
        _directory.Records[7] = TestRecord.Summary(7, status: status, isTerminal: terminal);
        _clock.Advance(TestRecord.ReviewSla);

        var result = await Scan().RunAsync(CancellationToken.None);

        Assert.Equal(1, result.Closed);
        Assert.False(Deadline(7).IsOpen);
        Assert.Empty(_notifications.Sent);
    }

    [Fact]
    public async Task A_deadline_whose_record_is_gone_is_closed()
    {
        _store.Seed(7, TestRecord.Start);
        _clock.Advance(TestRecord.ReviewSla);

        Assert.Equal(1, (await Scan().RunAsync(CancellationToken.None)).Closed);
        Assert.False(Deadline(7).IsOpen);
    }

    [Fact]
    public async Task A_breach_whose_SLA_was_removed_is_claimed_without_a_notification()
    {
        UnderReview(7, assignee: Assignee);
        _policies = new SlaPolicyCatalog([]);
        _clock.Advance(TestRecord.ReviewSla);

        var result = await Scan().RunAsync(CancellationToken.None);

        Assert.Equal(1, result.Skipped);
        Assert.NotNull(Deadline(7).EscalatedAt);
        Assert.Empty(_notifications.Sent);
    }

    [Fact]
    public async Task Marking_goes_on_past_one_batch()
    {
        var count = DeadlinesLimits.ScanBatchSize + 5;
        for (var id = 1; id <= count; id++)
        {
            UnderReview(id, assignee: Assignee);
        }

        _clock.Advance(TestRecord.ReviewSla);

        var result = await Scan().RunAsync(CancellationToken.None);

        Assert.Equal(count, result.Breached);
        Assert.Equal(DeadlinesLimits.ScanBatchSize, result.Escalated); // one escalation batch per run
        Assert.Equal(count, (await Scan().RunAsync(CancellationToken.None)).Escalated + DeadlinesLimits.ScanBatchSize);
    }

    [Fact]
    public async Task Without_a_record_directory_breaches_are_marked_and_wait_for_their_escalation()
    {
        UnderReview(7, assignee: Assignee);
        _withoutDirectory = true;
        _clock.Advance(TestRecord.ReviewSla);

        var result = await Scan().RunAsync(CancellationToken.None);

        Assert.Equal(new SlaScanResult(Breached: 1, Escalated: 0, Unaddressed: 0, Skipped: 0, Closed: 0), result);
        Assert.True(Deadline(7).AwaitsEscalation);
        Assert.Empty(_notifications.Sent);
    }

    [Fact]
    public async Task A_closed_deadline_never_breaches()
    {
        UnderReview(7, assignee: Assignee).Close(TestRecord.Start.AddDays(1));
        _clock.Advance(TestRecord.ReviewSla);

        Assert.Equal(0, (await Scan().RunAsync(CancellationToken.None)).Breached);
        Assert.Empty(_notifications.Sent);
    }

    [Fact]
    public async Task The_job_runs_the_scan_under_its_key_and_schedule()
    {
        UnderReview(7, assignee: Assignee);
        _clock.Advance(TestRecord.ReviewSla);
        var job = new SlaScanJob(Scan(), NullLogger<SlaScanJob>.Instance);

        await job.RunAsync(new JobRunContext(job.Key, RunId: 1, JobTrigger.Schedule, CorrelationId: "c0ffee"), CancellationToken.None);

        Assert.True(JobKeyFormat.IsValid(job.Key));
        Assert.Equal(DeadlinesJobKeys.SlaScan, job.Key);
        Assert.Equal(DeadlinesSchedules.EveryFiveMinutes, job.Cron);
        Assert.False(string.IsNullOrWhiteSpace(job.NameAr));
        Assert.False(string.IsNullOrWhiteSpace(job.NameEn));
        Assert.Single(_notifications.Sent);
    }

    [Fact]
    public async Task A_failed_scan_fails_the_job_run() =>
        await Assert.ThrowsAsync<InvalidOperationException>(async () =>
        {
            UnderReview(7, assignee: Assignee);
            _notifications.FailFor.Add(Assignee);
            _clock.Advance(TestRecord.ReviewSla);
            await new SlaScanJob(Scan(), NullLogger<SlaScanJob>.Instance)
                .RunAsync(new JobRunContext(DeadlinesJobKeys.SlaScan, RunId: 1, JobTrigger.Manual, CorrelationId: "c0ffee"), CancellationToken.None);
        });

    private RecordDeadline UnderReview(long recordId, long? assignee = null)
    {
        _directory.Records[recordId] = TestRecord.Summary(recordId);
        if (assignee is { } userId)
        {
            _assignments.Assign(recordId, userId);
        }

        return _store.Seed(recordId, TestRecord.Start);
    }

    private RecordDeadline Deadline(long recordId) => _store.Saved.Single(deadline => deadline.RecordId == recordId);

    private SlaScan Scan()
    {
        var recipients = new EscalationRecipients([_assignments], [_holders], [new RegistrationSource(TestRecord.Registration())]);
        var escalations = new SlaEscalations(
            _store,
            _policies,
            new BreachLoader(_withoutDirectory ? [] : [_directory], recipients),
            new SlaEscalationNotifier(_notifications, new FakeWorkflows(TestRecord.Workflow())),
            NullLogger<SlaEscalations>.Instance);
        return new SlaScan(_store, escalations, _clock);
    }
}
