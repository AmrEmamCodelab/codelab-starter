using Starter.Application.Workflow;
using Starter.Domain.Workflow;
using Starter.Workflow.Testing;

namespace Starter.Application.Tests.Workflow;

/// <summary>The <see cref="TransitionFixture"/> kit: a whole test-record path, and every expectation failing loudly.</summary>
public sealed class TransitionFixtureTests
{
    private static readonly TransitionFixture Requester = TransitionFixture.For(TestRecordWorkflow.Catalog)
        .WithPermissions(TestRecordPermissionKeys.Create).AsOwner();

    private static readonly TransitionFixture Decider = TransitionFixture.For(TestRecordWorkflow.Catalog)
        .WithPermissions(TestRecordPermissionKeys.Review, TestRecordPermissionKeys.Decide);

    [Fact]
    public void A_fixture_starts_at_the_initial_status_without_permissions_or_ownership()
    {
        var fixture = TransitionFixture.For(TestRecordWorkflow.Catalog);

        Assert.Equal(TestRecordStatuses.Draft, fixture.Status);
        Assert.Empty(fixture.Permissions);
        Assert.False(fixture.IsOwner);
        fixture.ExpectAllowedActions();
    }

    [Fact]
    public void The_return_and_resubmit_loop_walks_end_to_end()
    {
        var submitted = Requester.Do(TestRecordActions.Submit).ExpectStatus(TestRecordStatuses.Submitted);
        var returned = Decider.From(submitted.Status).Do(TestRecordActions.Take).ExpectStatus(TestRecordStatuses.InReview)
            .Do(TestRecordActions.Return).ExpectRefused(WorkflowRefusal.ReasonRequired)
            .Do(TestRecordActions.Return, "page 2 is missing").ExpectStatus(TestRecordStatuses.Returned);

        Requester.From(returned.Status).ExpectAllowedActions(TestRecordActions.Resubmit)
            .Do(TestRecordActions.Resubmit).ExpectStatus(TestRecordStatuses.Submitted);
    }

    [Fact]
    public void An_accepted_record_is_terminal_and_offers_nothing()
    {
        var accepted = Decider.From(TestRecordStatuses.InReview).Do(TestRecordActions.Accept).ExpectTerminal().ExpectStatus(TestRecordStatuses.Accepted);

        accepted.ExpectAllowedActions();
        Requester.From(TestRecordStatuses.Accepted).ExpectAllowedActions();
    }

    [Fact]
    public void A_declined_record_offers_the_requester_no_resubmit() =>
        Decider.From(TestRecordStatuses.InReview).Do(TestRecordActions.Decline, "duplicate").ExpectStatus(TestRecordStatuses.Declined)
            .WithPermissions(TestRecordPermissionKeys.Create).AsOwner().ExpectAllowedActions()
            .Do(TestRecordActions.Resubmit).ExpectRefused(WorkflowRefusal.NotAllowedFromStatus);

    [Fact]
    public void The_side_narrows_the_expected_offer()
    {
        var everything = TransitionFixture.For(TestRecordWorkflow.Catalog).From(TestRecordStatuses.Submitted)
            .WithPermissions(TestRecordPermissionKeys.Create, TestRecordPermissionKeys.Review).AsOwner();

        everything.ExpectAllowedActions(TestRecordActions.Withdraw);
        everything.OnSide(TransitionSide.Staff).ExpectAllowedActions();
        everything.OnSide(TransitionSide.Requester).ExpectAllowedActions(TestRecordActions.Withdraw);
        everything.AsNonOwner().ExpectAllowedActions(TestRecordActions.Take);
        everything.AsNonOwner().OnSide(TransitionSide.Staff).ExpectAllowedActions(TestRecordActions.Take);
    }

    [Fact]
    public void Do_acts_from_the_requester_side_as_the_owner_and_the_staff_side_otherwise()
    {
        var reviewer = TransitionFixture.For(TestRecordWorkflow.Catalog).From(TestRecordStatuses.Submitted)
            .WithPermissions(TestRecordPermissionKeys.Create, TestRecordPermissionKeys.Review);

        reviewer.AsOwner().Do(TestRecordActions.Take).ExpectRefused(WorkflowRefusal.Forbidden);
        reviewer.AsOwner().OnSide(TransitionSide.Staff).Do(TestRecordActions.Take).ExpectRefused(WorkflowRefusal.Forbidden);
        reviewer.AsNonOwner().Do(TestRecordActions.Take).ExpectStatus(TestRecordStatuses.InReview);
        reviewer.AsOwner().Do(TestRecordActions.Withdraw).ExpectStatus(TestRecordStatuses.Withdrawn);
    }

    [Fact]
    public void A_wrong_landing_status_fails() =>
        Assert.Throws<WorkflowExpectationException>(() => Requester.Do(TestRecordActions.Submit).ExpectStatus(TestRecordStatuses.InReview));

    [Fact]
    public void Expecting_a_refused_action_to_land_fails()
    {
        var failure = Assert.Throws<WorkflowExpectationException>(() => Decider.Do(TestRecordActions.Submit).ExpectStatus(TestRecordStatuses.Submitted));

        Assert.Contains(nameof(WorkflowRefusal.Forbidden), failure.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Expecting_a_refusal_of_an_allowed_action_fails() =>
        Assert.Throws<WorkflowExpectationException>(() => Requester.Do(TestRecordActions.Submit).ExpectRefused(WorkflowRefusal.Forbidden));

    [Fact]
    public void Expecting_the_wrong_refusal_fails() =>
        Assert.Throws<WorkflowExpectationException>(() => Decider.Do(TestRecordActions.Submit).ExpectRefused(WorkflowRefusal.ReasonRequired));

    [Fact]
    public void Expecting_terminal_on_an_open_landing_fails() =>
        Assert.Throws<WorkflowExpectationException>(() => Requester.Do(TestRecordActions.Submit).ExpectTerminal());

    [Fact]
    public void Expecting_the_wrong_allowed_actions_fails()
    {
        var failure = Assert.Throws<WorkflowExpectationException>(() => Requester.ExpectAllowedActions(TestRecordActions.Submit));

        Assert.Contains("withdraw", failure.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void A_catalog_without_an_initial_status_cannot_start_a_fixture()
    {
        var statuses = TestRecordWorkflow.Statuses.Select(status => status with { IsInitial = false }).ToList();

        Assert.Throws<WorkflowExpectationException>(() => TransitionFixture.For(new TransitionCatalog(TestRecordWorkflow.RecordType, statuses, TestRecordWorkflow.Rules)));
    }

    [Fact]
    public void The_broken_fixtures_validate_against_their_known_permissions() =>
        Assert.Empty(TransitionCatalogValidator.Validate(TestRecordWorkflow.Catalog, BrokenCatalogs.KnownPermissions));
}
