using Microsoft.Extensions.Time.Testing;
using Starter.Application.Records;
using Starter.Modules.Deadlines.Application;
using Starter.Modules.Deadlines.Tests.Fakes;

namespace Starter.Modules.Deadlines.Tests.Application;

public sealed class SlaPolicyCatalogTests
{
    [Fact]
    public void Find_returns_the_definition_of_a_record_type_and_status()
    {
        var catalog = new SlaPolicyCatalog([new PolicySource(TestRecord.ReviewDefinition())]);

        Assert.Equal(TestRecord.ReviewSla, catalog.Find(TestRecord.Type, TestRecord.UnderReview)!.Duration);
        Assert.Null(catalog.Find(TestRecord.Type, TestRecord.Submitted));
        Assert.Null(catalog.Find(TestRecord.OtherType, TestRecord.UnderReview));
    }

    [Fact]
    public void Definitions_from_every_source_are_read()
    {
        var catalog = new SlaPolicyCatalog(
            [new PolicySource(TestRecord.ReviewDefinition()), new PolicySource(TestRecord.ReviewDefinition(TestRecord.OtherType))]);

        Assert.Equal(2, catalog.All.Count);
        Assert.NotNull(catalog.Find(TestRecord.OtherType, TestRecord.UnderReview));
        Assert.Empty(catalog.FindViolations(new FakeWorkflows()));
    }

    [Fact]
    public void Two_definitions_of_one_status_are_a_violation_and_the_first_wins()
    {
        var longer = new SlaDefinition(TestRecord.Type, TestRecord.UnderReview, TimeSpan.FromDays(9), TimeSpan.Zero, TestRecord.EscalationTemplate);
        var catalog = new SlaPolicyCatalog([new PolicySource(TestRecord.ReviewDefinition()), new PolicySource(longer)]);

        var violation = Assert.Single(catalog.FindViolations(new FakeWorkflows()));

        Assert.Equal(SlaPolicyViolationKind.DuplicateStatus, violation.Kind);
        Assert.Equal(TestRecord.ReviewSla, catalog.Find(TestRecord.Type, TestRecord.UnderReview)!.Duration);
    }

    [Theory]
    [InlineData(42, SlaPolicyViolationKind.UndefinedStatus)]
    [InlineData(TestRecord.Closed, SlaPolicyViolationKind.TerminalStatus)]
    public void A_status_the_workflow_does_not_define_or_that_is_terminal_is_a_violation(int status, SlaPolicyViolationKind kind)
    {
        var definition = new SlaDefinition(TestRecord.Type, status, TestRecord.ReviewSla, TestRecord.ReviewDueSoon, TestRecord.EscalationTemplate);
        var catalog = new SlaPolicyCatalog([new PolicySource(definition)]);

        var violation = Assert.Single(catalog.FindViolations(new FakeWorkflows(TestRecord.Workflow())));

        Assert.Equal(new SlaPolicyViolation(TestRecord.Type, status, kind), violation);
        Assert.Contains(TestRecord.Type, violation.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void A_non_terminal_workflow_status_is_valid() =>
        Assert.Empty(new SlaPolicyCatalog([new PolicySource(TestRecord.ReviewDefinition())]).FindViolations(new FakeWorkflows(TestRecord.Workflow())));
}

public sealed class DeadlineTransitionObserverTests
{
    private readonly InMemoryDeadlineStore _store = new();

    private DeadlineTransitionObserver Observer => new(new SlaPolicyCatalog([new PolicySource(TestRecord.ReviewDefinition())]), _store);

    [Fact]
    public async Task Entering_a_status_with_an_SLA_opens_a_deadline_from_the_transition_time()
    {
        await Observer.OnTransitionedAsync(TestRecord.Transition(7, TestRecord.Submitted, TestRecord.UnderReview, TestRecord.Start), CancellationToken.None);

        var deadline = Assert.Single(_store.Open);
        Assert.Equal(7, deadline.RecordId);
        Assert.Equal(TestRecord.Start, deadline.StartedAt);
        Assert.Equal(TestRecord.Start + TestRecord.ReviewSla, deadline.DueAt);
    }

    [Fact]
    public async Task Entering_a_status_without_an_SLA_writes_nothing()
    {
        await Observer.OnTransitionedAsync(TestRecord.Transition(7, TestRecord.Draft, TestRecord.Submitted, TestRecord.Start), CancellationToken.None);

        Assert.Empty(_store.Saved);
        Assert.Equal(0, _store.Saves);
    }

    [Fact]
    public async Task Leaving_the_status_closes_the_deadline_at_the_transition_time()
    {
        await Observer.OnTransitionedAsync(TestRecord.Transition(7, TestRecord.Submitted, TestRecord.UnderReview, TestRecord.Start), CancellationToken.None);
        var left = TestRecord.Start + TimeSpan.FromHours(5);

        await Observer.OnTransitionedAsync(TestRecord.Transition(7, TestRecord.UnderReview, TestRecord.Returned, left), CancellationToken.None);

        var deadline = Assert.Single(_store.Saved);
        Assert.Equal(left, deadline.ClosedAt);
        Assert.Empty(_store.Open);
    }

    [Fact]
    public async Task A_terminal_transition_closes_the_deadline()
    {
        await Observer.OnTransitionedAsync(TestRecord.Transition(7, TestRecord.Submitted, TestRecord.UnderReview, TestRecord.Start), CancellationToken.None);

        await Observer.OnTransitionedAsync(TestRecord.Transition(7, TestRecord.UnderReview, TestRecord.Closed, TestRecord.Start, toTerminal: true), CancellationToken.None);

        Assert.Empty(_store.Open);
    }

    [Fact]
    public async Task A_replayed_transition_writes_nothing_more()
    {
        var entered = TestRecord.Transition(7, TestRecord.Submitted, TestRecord.UnderReview, TestRecord.Start);
        await Observer.OnTransitionedAsync(entered, CancellationToken.None);
        var saves = _store.Saves;

        await Observer.OnTransitionedAsync(entered, CancellationToken.None);

        Assert.Single(_store.Saved);
        Assert.Equal(saves, _store.Saves);
    }

    [Fact]
    public async Task Re_entering_the_status_starts_a_new_deadline_and_keeps_the_closed_one()
    {
        await Observer.OnTransitionedAsync(TestRecord.Transition(7, TestRecord.Submitted, TestRecord.UnderReview, TestRecord.Start), CancellationToken.None);
        await Observer.OnTransitionedAsync(TestRecord.Transition(7, TestRecord.UnderReview, TestRecord.Returned, TestRecord.Start.AddDays(1)), CancellationToken.None);
        var again = TestRecord.Start.AddDays(2);

        await Observer.OnTransitionedAsync(TestRecord.Transition(7, TestRecord.Returned, TestRecord.UnderReview, again), CancellationToken.None);

        Assert.Equal(2, _store.Saved.Count);
        Assert.Equal(again, Assert.Single(_store.Open).StartedAt);
    }

    [Fact]
    public async Task Another_record_type_with_the_same_status_value_gets_no_deadline()
    {
        await Observer.OnTransitionedAsync(
            TestRecord.Transition(7, TestRecord.Submitted, TestRecord.UnderReview, TestRecord.Start) with { RecordType = TestRecord.OtherType },
            CancellationToken.None);

        Assert.Empty(_store.Saved);
    }
}

public sealed class RecordSlaReaderTests
{
    private readonly InMemoryDeadlineStore _store = new();
    private readonly FakeTimeProvider _clock = new(TestRecord.Start);

    [Fact]
    public async Task The_badge_follows_the_clock_from_on_track_to_due_soon_to_breached()
    {
        _store.Seed(7, TestRecord.Start);
        var reader = new RecordSlaReader(_store, _clock);
        var due = TestRecord.Start + TestRecord.ReviewSla;

        Assert.Equal(new SlaBadge(SlaState.OnTrack, due), (await reader.BadgesAsync([7], CancellationToken.None))[7]);
        _clock.Advance(TimeSpan.FromDays(2));
        Assert.Equal(SlaState.DueSoon, (await reader.BadgesAsync([7], CancellationToken.None))[7].State);
        _clock.Advance(TimeSpan.FromDays(1));
        Assert.Equal(SlaState.Breached, (await reader.BadgesAsync([7], CancellationToken.None))[7].State);
    }

    [Fact]
    public async Task Records_without_an_open_deadline_are_absent()
    {
        _store.Seed(7, TestRecord.Start).Close(TestRecord.Start.AddHours(1));
        _store.Seed(8, TestRecord.Start);

        var badges = await new RecordSlaReader(_store, _clock).BadgesAsync([7, 8, 9], CancellationToken.None);

        Assert.Equal([8L], badges.Keys);
    }

    [Fact]
    public async Task No_ids_read_nothing() =>
        Assert.Empty(await new RecordSlaReader(_store, _clock).BadgesAsync([], CancellationToken.None));
}

public sealed class EscalationRecipientsTests
{
    private const long Assignee = 41;
    private const long OrganizationId = 3;

    private readonly FakeAssignments _assignments = new();
    private readonly FakeHolders _holders = new();

    [Fact]
    public async Task The_assignee_receives_the_escalation_of_an_assigned_record()
    {
        _assignments.Assign(7, Assignee);
        _holders.Holders[(TestRecord.QueuePermission, null)] = [51, 52];

        var recipients = await Recipients().ResolveAsync([TestRecord.Summary(7)], CancellationToken.None);

        Assert.Equal([Assignee], recipients[7]);
        Assert.Empty(_holders.Calls);
    }

    [Fact]
    public async Task The_queue_staff_of_the_records_organization_receive_it_while_unassigned()
    {
        _holders.Holders[(TestRecord.QueuePermission, OrganizationId)] = [52, 51, 52];

        var recipients = await Recipients().ResolveAsync(
            [TestRecord.Summary(7, organizationId: OrganizationId), TestRecord.Summary(8, organizationId: OrganizationId)],
            CancellationToken.None);

        Assert.Equal([51L, 52L], recipients[7]);
        Assert.Equal([51L, 52L], recipients[8]);
        Assert.Single(_holders.Calls); // one lookup per record type and organization
    }

    [Fact]
    public async Task Without_a_registration_or_a_staff_directory_nobody_is_named()
    {
        var withoutRegistration = new EscalationRecipients([_assignments], [_holders], []);
        var withoutDirectory = new EscalationRecipients([_assignments], [], [new RegistrationSource(TestRecord.Registration())]);

        Assert.Empty((await withoutRegistration.ResolveAsync([TestRecord.Summary(7)], CancellationToken.None))[7]);
        Assert.Empty((await withoutDirectory.ResolveAsync([TestRecord.Summary(7)], CancellationToken.None))[7]);
    }

    [Fact]
    public async Task Without_an_assignment_reader_the_queue_staff_are_used()
    {
        _holders.Holders[(TestRecord.QueuePermission, null)] = [51];
        var recipients = new EscalationRecipients([], [_holders], [new RegistrationSource(TestRecord.Registration())]);

        Assert.Equal([51L], (await recipients.ResolveAsync([TestRecord.Summary(7)], CancellationToken.None))[7]);
    }

    [Fact]
    public async Task No_records_ask_no_port()
    {
        Assert.Empty(await Recipients().ResolveAsync([], CancellationToken.None));
        Assert.Equal(0, _assignments.Calls);
    }

    private EscalationRecipients Recipients() => new([_assignments], [_holders], [new RegistrationSource(TestRecord.Registration())]);
}
