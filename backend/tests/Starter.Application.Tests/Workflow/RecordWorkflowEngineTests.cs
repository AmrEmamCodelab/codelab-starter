using Starter.Application.Workflow;
using Starter.Domain.Workflow;
using Starter.Workflow.Testing;

namespace Starter.Application.Tests.Workflow;

/// <summary>Allowed actions derived from the catalog, the decision order and the reason rules.</summary>
public sealed class RecordWorkflowEngineTests
{
    private static readonly TransitionCatalog Catalog = TestRecordWorkflow.Catalog;

    [Fact]
    public void The_owner_with_the_create_permission_is_offered_the_requester_actions_of_a_draft()
    {
        var actions = RecordWorkflowEngine.AllowedActions(Catalog, TestRecordStatuses.Draft, Holds(TestRecordPermissionKeys.Create), isOwner: true, TransitionSide.Requester);

        Assert.Equal([TestRecordActions.Submit, TestRecordActions.Withdraw], actions.Select(action => action.ActionKey));
    }

    [Fact]
    public void An_allowed_action_carries_its_label_reason_and_landing()
    {
        var withdraw = RecordWorkflowEngine.AllowedActions(Catalog, TestRecordStatuses.Draft, Holds(TestRecordPermissionKeys.Create), isOwner: true, TransitionSide.Requester)[1];

        Assert.Equal(new AllowedAction(TestRecordActions.Withdraw, "testRecords.action.withdraw", ReasonRequirement.Optional, TestRecordWorkflow.ReasonField, TestRecordStatuses.Withdrawn, ToTerminal: true), withdraw);
    }

    [Fact]
    public void A_requester_who_does_not_own_the_record_is_offered_nothing() =>
        Assert.Empty(RecordWorkflowEngine.AllowedActions(Catalog, TestRecordStatuses.Draft, Holds(TestRecordPermissionKeys.Create), isOwner: false, TransitionSide.Requester));

    [Fact]
    public void Staff_see_only_the_actions_their_permissions_allow()
    {
        var reviewer = RecordWorkflowEngine.AllowedActions(Catalog, TestRecordStatuses.InReview, Holds(TestRecordPermissionKeys.Review), isOwner: false, TransitionSide.Staff);
        var decider = RecordWorkflowEngine.AllowedActions(Catalog, TestRecordStatuses.InReview, Holds(TestRecordPermissionKeys.Decide), isOwner: false, TransitionSide.Staff);

        Assert.Empty(reviewer);
        Assert.Equal([TestRecordActions.Return, TestRecordActions.Accept, TestRecordActions.Decline], decider.Select(action => action.ActionKey));
    }

    [Fact]
    public void The_side_filters_the_offer_even_when_the_permissions_allow_more() =>
        Assert.Empty(RecordWorkflowEngine.AllowedActions(Catalog, TestRecordStatuses.Draft, Holds(TestRecordPermissionKeys.Create), isOwner: true, TransitionSide.Staff));

    [Theory]
    [InlineData(TestRecordStatuses.Accepted)]
    [InlineData(TestRecordStatuses.Declined)]
    [InlineData(TestRecordStatuses.Withdrawn)]
    public void A_terminal_status_offers_no_action_to_anyone(int terminal)
    {
        foreach (var side in Enum.GetValues<TransitionSide>())
        {
            Assert.Empty(RecordWorkflowEngine.AllowedActions(Catalog, terminal, _ => true, isOwner: true, side));
        }
    }

    [Fact]
    public void A_terminal_status_offers_nothing_even_when_a_broken_catalog_has_a_rule_out_of_it()
    {
        var rules = TestRecordWorkflow.Rules.Append(TestRecordWorkflow.Rules[0] with { ActionKey = "reopen", FromStatus = TestRecordStatuses.Accepted }).ToList();
        var catalog = new TransitionCatalog(TestRecordWorkflow.RecordType, TestRecordWorkflow.Statuses, rules);

        Assert.Empty(RecordWorkflowEngine.AllowedActions(catalog, TestRecordStatuses.Accepted, _ => true, isOwner: true, TransitionSide.Requester));
        Assert.Equal(WorkflowRefusal.NotAllowedFromStatus, Refusal(RecordWorkflowEngine.Decide(catalog, TestRecordStatuses.Accepted, "reopen", reason: null, Owner())));
    }

    [Fact]
    public void An_undefined_status_offers_nothing()
    {
        const int undefined = 99;

        Assert.Empty(RecordWorkflowEngine.AllowedActions(Catalog, undefined, _ => true, isOwner: true, TransitionSide.Requester));
    }

    [Fact]
    public void A_rule_without_a_permission_key_is_never_offered()
    {
        var actions = RecordWorkflowEngine.AllowedActions(BrokenCatalogs.WithoutPermission, TestRecordStatuses.Submitted, _ => true, isOwner: true, TransitionSide.Staff);

        Assert.Empty(actions);
    }

    [Fact]
    public void An_allowed_transition_returns_its_rule() =>
        TransitionFixture.For(Catalog).WithPermissions(TestRecordPermissionKeys.Create).AsOwner()
            .Do(TestRecordActions.Submit).ExpectStatus(TestRecordStatuses.Submitted);

    [Fact]
    public void An_unknown_action_is_refused_first() =>
        Assert.Equal(WorkflowRefusal.ActionUnknown, Refusal(RecordWorkflowEngine.Decide(Catalog, TestRecordStatuses.Draft, "teleport", reason: null, Owner())));

    [Fact]
    public void An_action_key_is_case_sensitive() =>
        Assert.Equal(WorkflowRefusal.ActionUnknown, Refusal(RecordWorkflowEngine.Decide(Catalog, TestRecordStatuses.Draft, "Submit", reason: null, Owner())));

    [Fact]
    public void A_caller_who_may_never_take_the_action_is_forbidden_whatever_the_status()
    {
        var reviewer = new WorkflowStanding(Holds(TestRecordPermissionKeys.Review), IsOwner: false, TransitionSide.Staff);

        Assert.Equal(WorkflowRefusal.Forbidden, Refusal(RecordWorkflowEngine.Decide(Catalog, TestRecordStatuses.Accepted, TestRecordActions.Accept, reason: null, reviewer)));
    }

    [Fact]
    public void A_non_owner_is_forbidden_an_owner_only_action()
    {
        var stranger = new WorkflowStanding(Holds(TestRecordPermissionKeys.Create), IsOwner: false, TransitionSide.Requester);

        Assert.Equal(WorkflowRefusal.Forbidden, Refusal(RecordWorkflowEngine.Decide(Catalog, TestRecordStatuses.Draft, TestRecordActions.Submit, reason: null, stranger)));
    }

    [Fact]
    public void A_permitted_action_from_the_wrong_status_is_not_allowed() =>
        Assert.Equal(WorkflowRefusal.NotAllowedFromStatus, Refusal(RecordWorkflowEngine.Decide(Catalog, TestRecordStatuses.Submitted, TestRecordActions.Accept, reason: null, Staff())));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void A_required_reason_must_not_be_blank(string? reason)
    {
        var decision = RecordWorkflowEngine.Decide(Catalog, TestRecordStatuses.InReview, TestRecordActions.Return, reason, Staff());

        var refused = Assert.IsType<TransitionDecision.Refused>(decision);
        Assert.Equal(WorkflowRefusal.ReasonRequired, refused.Refusal);
        Assert.Equal(TestRecordWorkflow.ReasonField, refused.ReasonFieldKey);
    }

    [Fact]
    public void A_reason_is_trimmed()
    {
        var decision = RecordWorkflowEngine.Decide(Catalog, TestRecordStatuses.InReview, TestRecordActions.Return, "  missing page  ", Staff());

        Assert.Equal("missing page", Assert.IsType<TransitionDecision.Allowed>(decision).Reason);
    }

    [Fact]
    public void A_reason_of_the_maximum_length_is_accepted()
    {
        var reason = new string('x', WorkflowLimits.MaxReasonLength);

        Assert.IsType<TransitionDecision.Allowed>(RecordWorkflowEngine.Decide(Catalog, TestRecordStatuses.InReview, TestRecordActions.Decline, reason, Staff()));
    }

    [Theory]
    [InlineData(TestRecordStatuses.InReview, TestRecordActions.Decline)]
    [InlineData(TestRecordStatuses.Draft, TestRecordActions.Withdraw)]
    public void A_reason_longer_than_the_maximum_is_refused(int from, string action)
    {
        var reason = new string('x', WorkflowLimits.MaxReasonLength + 1);

        var refused = Assert.IsType<TransitionDecision.Refused>(RecordWorkflowEngine.Decide(Catalog, from, action, reason, StandingFor(action)));

        Assert.Equal(WorkflowRefusal.ReasonTooLong, refused.Refusal);
        Assert.Equal(TestRecordWorkflow.ReasonField, refused.ReasonFieldKey);
    }

    [Fact]
    public void An_optional_reason_may_be_left_out()
    {
        var decision = RecordWorkflowEngine.Decide(Catalog, TestRecordStatuses.Draft, TestRecordActions.Withdraw, reason: null, Owner());

        Assert.Null(Assert.IsType<TransitionDecision.Allowed>(decision).Reason);
    }

    [Fact]
    public void A_reason_sent_to_an_action_that_takes_none_is_dropped_even_when_too_long()
    {
        var reason = new string('x', WorkflowLimits.MaxReasonLength + 1);

        var allowed = Assert.IsType<TransitionDecision.Allowed>(RecordWorkflowEngine.Decide(Catalog, TestRecordStatuses.InReview, TestRecordActions.Accept, reason, Staff()));

        Assert.Null(allowed.Reason);
    }

    [Fact]
    public void The_same_action_key_resolves_to_the_rule_of_the_current_status()
    {
        var fromSubmitted = Assert.IsType<TransitionDecision.Allowed>(RecordWorkflowEngine.Decide(Catalog, TestRecordStatuses.Submitted, TestRecordActions.Withdraw, reason: null, Owner()));

        Assert.Equal(TestRecordStatuses.Submitted, fromSubmitted.Rule.FromStatus);
        Assert.True(fromSubmitted.Rule.Notification!.NotifyAssignee);
    }

    [Theory]
    [InlineData(TestRecordStatuses.Submitted, TestRecordActions.Take)]
    [InlineData(TestRecordStatuses.InReview, TestRecordActions.Accept)]
    [InlineData(TestRecordStatuses.InReview, TestRecordActions.Return)]
    public void The_owner_holding_every_staff_key_is_forbidden_every_staff_action_on_their_own_record(int from, string action)
    {
        Assert.Equal(WorkflowRefusal.Forbidden, Refusal(RecordWorkflowEngine.Decide(Catalog, from, action, "reason", Owner())));
        Assert.Equal(WorkflowRefusal.Forbidden, Refusal(RecordWorkflowEngine.Decide(Catalog, from, action, "reason", Owner() with { Side = TransitionSide.Staff })));
    }

    [Fact]
    public void The_owner_holding_every_staff_key_is_offered_no_staff_action()
    {
        Assert.Empty(RecordWorkflowEngine.AllowedActions(Catalog, TestRecordStatuses.Submitted, _ => true, isOwner: true, TransitionSide.Staff));
        Assert.Empty(RecordWorkflowEngine.AllowedActions(Catalog, TestRecordStatuses.InReview, _ => true, isOwner: true, TransitionSide.Staff));
    }

    [Fact]
    public void A_caller_is_forbidden_a_rule_of_the_other_side()
    {
        var requesterSideStaff = new WorkflowStanding(_ => true, IsOwner: false, TransitionSide.Requester);

        Assert.Equal(WorkflowRefusal.Forbidden, Refusal(RecordWorkflowEngine.Decide(Catalog, TestRecordStatuses.InReview, TestRecordActions.Accept, reason: null, requesterSideStaff)));
        Assert.Equal(WorkflowRefusal.Forbidden, Refusal(RecordWorkflowEngine.Decide(Catalog, TestRecordStatuses.Draft, TestRecordActions.Submit, reason: null, Owner() with { Side = TransitionSide.Staff })));
    }

    [Fact]
    public void The_engine_rejects_null_arguments()
    {
        Assert.Throws<ArgumentNullException>(() => RecordWorkflowEngine.AllowedActions(null!, 1, _ => true, isOwner: true, TransitionSide.Staff));
        Assert.Throws<ArgumentNullException>(() => RecordWorkflowEngine.AllowedActions(Catalog, 1, null!, isOwner: true, TransitionSide.Staff));
        Assert.Throws<ArgumentNullException>(() => RecordWorkflowEngine.Decide(null!, 1, TestRecordActions.Submit, reason: null, Owner()));
        Assert.Throws<ArgumentNullException>(() => RecordWorkflowEngine.Decide(Catalog, 1, TestRecordActions.Submit, reason: null, null!));
    }

    private static Func<string, bool> Holds(params string[] keys) => keys.Contains;

    /// <summary>The owner holding every key, acting from the requester screens.</summary>
    private static WorkflowStanding Owner() => new(_ => true, IsOwner: true, TransitionSide.Requester);

    /// <summary>A non-owner holding every key, acting from the staff screens.</summary>
    private static WorkflowStanding Staff() => new(_ => true, IsOwner: false, TransitionSide.Staff);

    private static WorkflowStanding StandingFor(string action) => action == TestRecordActions.Withdraw ? Owner() : Staff();

    private static WorkflowRefusal Refusal(TransitionDecision decision) => Assert.IsType<TransitionDecision.Refused>(decision).Refusal;
}
