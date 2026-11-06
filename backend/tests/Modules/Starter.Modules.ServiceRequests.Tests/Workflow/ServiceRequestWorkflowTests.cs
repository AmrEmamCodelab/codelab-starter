using Starter.Application.Workflow;
using Starter.Domain.Workflow;
using Starter.Modules.ServiceRequests.Application;
using Starter.Modules.ServiceRequests.Application.Workflow;
using Starter.Modules.ServiceRequests.Domain;
using Starter.Workflow.Testing;

namespace Starter.Modules.ServiceRequests.Tests.Workflow;

public sealed class ServiceRequestWorkflowTests
{
    private const string Reason = "Page 2 of the document is missing.";

    private static readonly IReadOnlySet<string> KnownKeys =
        new ServiceRequestsPermissionCatalogSource().Permissions.Select(permission => permission.Key).ToHashSet(StringComparer.Ordinal);

    private static TransitionFixture Requester(int status) =>
        TransitionFixture.For(ServiceRequestWorkflow.Catalog).From(status).WithPermissions(ServiceRequestsPermissionKeys.Create).AsOwner();

    private static TransitionFixture Reviewer(int status) =>
        TransitionFixture.For(ServiceRequestWorkflow.Catalog).From(status)
            .WithPermissions(ServiceRequestsPermissionKeys.Queue, ServiceRequestsPermissionKeys.Review, ServiceRequestsPermissionKeys.Decide)
            .AsNonOwner();

    [Fact]
    public void The_catalog_passes_the_kernel_validator_against_the_module_permissions()
    {
        Assert.Empty(TransitionCatalogValidator.Validate(ServiceRequestWorkflow.Catalog, KnownKeys));
    }

    [Fact]
    public void The_definition_names_the_record_type_of_its_catalog()
    {
        var definition = new ServiceRequestWorkflowDefinition();

        Assert.Equal(ServiceRequestRecord.RecordType, definition.RecordType);
        Assert.Same(ServiceRequestWorkflow.Catalog, definition.Catalog);
        Assert.Equal(ServiceRequestRecord.RecordType, definition.Catalog.RecordType);
    }

    [Fact]
    public void Draft_is_the_only_initial_status_and_the_three_decisions_are_terminal()
    {
        var statuses = ServiceRequestWorkflow.Statuses;

        Assert.Equal([ServiceRequestStatuses.Draft], statuses.Where(status => status.IsInitial).Select(status => status.Status));
        Assert.Equal(
            [ServiceRequestStatuses.Approved, ServiceRequestStatuses.Rejected, ServiceRequestStatuses.Cancelled],
            statuses.Where(status => status.IsTerminal).Select(status => status.Status));
        Assert.Equal(Enumerable.Range(ServiceRequestStatuses.Draft, ServiceRequestStatuses.Cancelled), statuses.Select(status => status.Status));
    }

    [Fact]
    public void The_happy_path_runs_from_draft_to_approved()
    {
        Requester(ServiceRequestStatuses.Draft)
            .ExpectAllowedActions(ServiceRequestActions.Submit, ServiceRequestActions.Cancel)
            .Do(ServiceRequestActions.Submit).ExpectStatus(ServiceRequestStatuses.Submitted);
        Reviewer(ServiceRequestStatuses.Submitted)
            .Do(ServiceRequestActions.StartReview).ExpectStatus(ServiceRequestStatuses.UnderReview)
            .ExpectAllowedActions(ServiceRequestActions.Approve, ServiceRequestActions.Reject, ServiceRequestActions.Return)
            .Do(ServiceRequestActions.Approve).ExpectTerminal().ExpectStatus(ServiceRequestStatuses.Approved)
            .ExpectAllowedActions();
    }

    [Fact]
    public void A_returned_request_is_resubmitted_and_reviewed_again()
    {
        Reviewer(ServiceRequestStatuses.UnderReview)
            .Do(ServiceRequestActions.Return, Reason).ExpectStatus(ServiceRequestStatuses.Returned);
        Requester(ServiceRequestStatuses.Returned)
            .ExpectAllowedActions(ServiceRequestActions.Resubmit)
            .Do(ServiceRequestActions.Resubmit).ExpectStatus(ServiceRequestStatuses.Resubmitted);
        Reviewer(ServiceRequestStatuses.Resubmitted)
            .ExpectAllowedActions(ServiceRequestActions.StartReview)
            .Do(ServiceRequestActions.StartReview).ExpectStatus(ServiceRequestStatuses.UnderReview);
    }

    [Fact]
    public void Return_and_reject_need_a_reason()
    {
        Reviewer(ServiceRequestStatuses.UnderReview)
            .Do(ServiceRequestActions.Return).ExpectRefused(WorkflowRefusal.ReasonRequired)
            .Do(ServiceRequestActions.Return, "   ").ExpectRefused(WorkflowRefusal.ReasonRequired)
            .Do(ServiceRequestActions.Reject).ExpectRefused(WorkflowRefusal.ReasonRequired)
            .Do(ServiceRequestActions.Reject, Reason).ExpectTerminal().ExpectStatus(ServiceRequestStatuses.Rejected)
            .ExpectAllowedActions();
    }

    [Fact]
    public void A_reason_longer_than_the_kernel_limit_is_refused()
    {
        Reviewer(ServiceRequestStatuses.UnderReview)
            .Do(ServiceRequestActions.Reject, new string('x', WorkflowLimits.MaxReasonLength + 1)).ExpectRefused(WorkflowRefusal.ReasonTooLong);
    }

    [Theory]
    [InlineData(ServiceRequestStatuses.Draft)]
    [InlineData(ServiceRequestStatuses.Submitted)]
    public void The_owner_cancels_a_draft_or_a_submitted_request_with_or_without_a_reason(int status)
    {
        Requester(status).Do(ServiceRequestActions.Cancel).ExpectTerminal().ExpectStatus(ServiceRequestStatuses.Cancelled).ExpectAllowedActions();
        Requester(status).Do(ServiceRequestActions.Cancel, Reason).ExpectStatus(ServiceRequestStatuses.Cancelled);
    }

    [Theory]
    [InlineData(ServiceRequestStatuses.UnderReview)]
    [InlineData(ServiceRequestStatuses.Returned)]
    [InlineData(ServiceRequestStatuses.Resubmitted)]
    public void A_request_under_review_or_later_cannot_be_cancelled(int status)
    {
        Requester(status).Do(ServiceRequestActions.Cancel).ExpectRefused(WorkflowRefusal.NotAllowedFromStatus);
    }

    [Theory]
    [InlineData(ServiceRequestStatuses.Approved)]
    [InlineData(ServiceRequestStatuses.Rejected)]
    [InlineData(ServiceRequestStatuses.Cancelled)]
    public void A_terminal_status_offers_no_action_to_anyone(int status)
    {
        TransitionFixture.For(ServiceRequestWorkflow.Catalog).From(status)
            .WithPermissions([.. KnownKeys]).AsOwner()
            .ExpectAllowedActions();
        Requester(status).Do(ServiceRequestActions.Resubmit).ExpectRefused(WorkflowRefusal.NotAllowedFromStatus);
    }

    [Fact]
    public void Requester_actions_need_the_ownership_and_the_create_permission()
    {
        Requester(ServiceRequestStatuses.Draft).AsNonOwner().Do(ServiceRequestActions.Submit).ExpectRefused(WorkflowRefusal.Forbidden);
        TransitionFixture.For(ServiceRequestWorkflow.Catalog).From(ServiceRequestStatuses.Draft).AsOwner()
            .Do(ServiceRequestActions.Submit).ExpectRefused(WorkflowRefusal.Forbidden);
    }

    [Fact]
    public void Staff_actions_need_their_permission_and_are_not_offered_to_the_requester()
    {
        Requester(ServiceRequestStatuses.Submitted).Do(ServiceRequestActions.StartReview).ExpectRefused(WorkflowRefusal.Forbidden);
        TransitionFixture.For(ServiceRequestWorkflow.Catalog).From(ServiceRequestStatuses.UnderReview)
            .WithPermissions(ServiceRequestsPermissionKeys.Review)
            .Do(ServiceRequestActions.Approve).ExpectRefused(WorkflowRefusal.Forbidden);
        Requester(ServiceRequestStatuses.Submitted).ExpectAllowedActions(ServiceRequestActions.Cancel);
    }

    [Fact]
    public void An_unknown_action_is_refused()
    {
        Requester(ServiceRequestStatuses.Draft).Do("archive").ExpectRefused(WorkflowRefusal.ActionUnknown);
    }

    [Fact]
    public void Every_status_has_its_three_audience_wording_keys()
    {
        foreach (var status in ServiceRequestWorkflow.Statuses)
        {
            Assert.Equal($"serviceRequests.status.{status.Key}.requester", status.Wording.RequesterKey);
            Assert.Equal($"serviceRequests.status.{status.Key}.staff", status.Wording.StaffKey);
            Assert.Equal($"serviceRequests.status.{status.Key}.admin", status.Wording.AdminKey);
        }
    }

    [Fact]
    public void Every_rule_has_a_label_and_a_notification_decision_with_a_known_template()
    {
        foreach (var rule in ServiceRequestWorkflow.Rules)
        {
            Assert.Equal($"serviceRequests.action.{rule.ActionKey}", rule.LabelKey);
            Assert.NotNull(rule.Notification);
            Assert.True(rule.Notification!.TemplateKey is null || ServiceRequestNotificationKeys.All.Contains(rule.Notification.TemplateKey), rule.Describe());
            Assert.Equal(rule.Notification.TemplateKey is not null, rule.Notification.NotifiesAnyone);
        }
    }

    [Fact]
    public void Only_cancelling_a_draft_notifies_nobody()
    {
        var silent = ServiceRequestWorkflow.Rules.Where(rule => rule.Notification == NotificationDecision.None).Select(rule => rule.Describe());

        Assert.Equal([$"{ServiceRequestActions.Cancel} ({ServiceRequestStatuses.Draft} -> {ServiceRequestStatuses.Cancelled})"], silent);
    }

    [Fact]
    public void Reason_rules_name_the_reason_field()
    {
        var withReason = ServiceRequestWorkflow.Rules.Where(rule => rule.Reason != ReasonRequirement.None).ToList();

        Assert.Equal(
            [ServiceRequestActions.Return, ServiceRequestActions.Reject, ServiceRequestActions.Cancel, ServiceRequestActions.Cancel],
            withReason.Select(rule => rule.ActionKey));
        Assert.All(withReason, rule => Assert.Equal(ServiceRequestWorkflow.ReasonField, rule.ReasonFieldKey));
        Assert.Equal(
            [ServiceRequestActions.Return, ServiceRequestActions.Reject],
            withReason.Where(rule => rule.Reason == ReasonRequirement.Required).Select(rule => rule.ActionKey));
    }

    [Fact]
    public void Requester_rules_need_ownership_and_staff_rules_do_not()
    {
        Assert.All(ServiceRequestWorkflow.Rules, rule =>
        {
            Assert.Equal(rule.Side == TransitionSide.Requester, rule.RequiresOwnership);
            Assert.Equal(rule.Side == TransitionSide.Requester, rule.PermissionKey == ServiceRequestsPermissionKeys.Create);
        });
    }

    [Fact]
    public void The_wording_key_refuses_an_undefined_audience()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => ServiceRequestWorkflow.WordingKey(ServiceRequestStatusKeys.Draft, (WorkflowAudience)0));
    }
}
