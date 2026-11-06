using Starter.Domain.Workflow;
using Starter.Workflow.Testing;

namespace Starter.Application.Tests.Workflow;

/// <summary>Every <see cref="CatalogViolationKind"/> is detected, and a valid catalog has none.</summary>
public sealed class TransitionCatalogValidatorTests
{
    private static readonly IReadOnlySet<string> Known = TestRecordPermissionKeys.All;

    [Fact]
    public void The_valid_test_record_catalog_has_no_violation() =>
        Assert.Empty(TransitionCatalogValidator.Validate(TestRecordWorkflow.Catalog, Known));

    [Fact]
    public void An_unreachable_state_is_reported()
    {
        var violation = Assert.Single(TransitionCatalogValidator.Validate(BrokenCatalogs.Unreachable, Known));

        Assert.Equal(CatalogViolationKind.UnreachableState, violation.Kind);
        Assert.Equal("status orphan (9)", violation.Subject);
    }

    [Fact]
    public void Removing_the_only_rule_into_a_state_makes_it_unreachable()
    {
        var catalog = Catalog(rules: TestRecordWorkflow.Rules.Where(rule => rule.ActionKey != TestRecordActions.Return).ToList());

        var violations = TransitionCatalogValidator.Validate(catalog, Known);

        Assert.Equal(CatalogViolationKind.UnreachableState, Assert.Single(violations).Kind);
        Assert.Contains("returned", violations[0].Subject, StringComparison.Ordinal);
    }

    [Fact]
    public void A_transition_without_a_permission_is_reported()
    {
        var violation = Assert.Single(TransitionCatalogValidator.Validate(BrokenCatalogs.WithoutPermission, Known));

        Assert.Equal(CatalogViolationKind.MissingPermission, violation.Kind);
        Assert.Equal("take (2 -> 3)", violation.Subject);
    }

    [Fact]
    public void A_permission_missing_from_the_permission_catalog_is_unknown()
    {
        var known = new HashSet<string>(StringComparer.Ordinal) { TestRecordPermissionKeys.Create, TestRecordPermissionKeys.Review };

        var violations = TransitionCatalogValidator.Validate(TestRecordWorkflow.Catalog, known);

        Assert.All(violations, violation => Assert.Equal(CatalogViolationKind.UnknownPermission, violation.Kind));
        Assert.Equal(3, violations.Count);
    }

    [Fact]
    public void A_required_reason_without_a_field_is_reported()
    {
        var violation = Assert.Single(TransitionCatalogValidator.Validate(BrokenCatalogs.ReasonWithoutField, Known));

        Assert.Equal(CatalogViolationKind.ReasonFieldMissing, violation.Kind);
        Assert.Equal("return (3 -> 4)", violation.Subject);
    }

    [Fact]
    public void An_optional_reason_without_a_field_is_allowed()
    {
        var rules = TestRecordWorkflow.Rules.Select(rule => rule.Reason == ReasonRequirement.Optional ? rule with { ReasonFieldKey = null } : rule).ToList();

        Assert.Empty(TransitionCatalogValidator.Validate(Catalog(rules: rules), Known));
    }

    [Fact]
    public void The_combined_broken_catalog_reports_all_three_defects()
    {
        var kinds = TransitionCatalogValidator.Validate(BrokenCatalogs.Combined, Known).Select(violation => violation.Kind).Order().ToArray();

        Assert.Equal([CatalogViolationKind.UnreachableState, CatalogViolationKind.MissingPermission, CatalogViolationKind.ReasonFieldMissing], kinds);
    }

    [Theory]
    [InlineData(WorkflowAudience.Requester)]
    [InlineData(WorkflowAudience.Staff)]
    [InlineData(WorkflowAudience.Admin)]
    public void A_status_without_the_wording_of_an_audience_is_reported(WorkflowAudience audience)
    {
        var statuses = TestRecordWorkflow.Statuses
            .Select(status => status.Status == TestRecordStatuses.Submitted ? status with { Wording = Blank(status.Wording, audience) } : status)
            .ToList();

        var violation = Assert.Single(TransitionCatalogValidator.Validate(Catalog(statuses: statuses), Known));

        Assert.Equal(CatalogViolationKind.MissingWording, violation.Kind);
        Assert.Contains(audience.ToString(), violation.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void A_status_without_any_wording_is_reported_once_per_audience()
    {
        var statuses = TestRecordWorkflow.Statuses
            .Select(status => status.Status == TestRecordStatuses.Accepted ? status with { Wording = null! } : status)
            .ToList();

        var violations = TransitionCatalogValidator.Validate(Catalog(statuses: statuses), Known);

        Assert.Equal(Enum.GetValues<WorkflowAudience>().Length, violations.Count(violation => violation.Kind == CatalogViolationKind.MissingWording));
    }

    [Fact]
    public void An_action_without_a_label_is_missing_wording()
    {
        var rules = TestRecordWorkflow.Rules.Select(rule => rule.ActionKey == TestRecordActions.Accept ? rule with { LabelKey = " " } : rule).ToList();

        Assert.Equal(CatalogViolationKind.MissingWording, Assert.Single(TransitionCatalogValidator.Validate(Catalog(rules: rules), Known)).Kind);
    }

    [Fact]
    public void A_rule_without_a_notification_decision_is_reported()
    {
        var rules = TestRecordWorkflow.Rules.Select(rule => rule.ActionKey == TestRecordActions.Take ? rule with { Notification = null } : rule).ToList();

        var violation = Assert.Single(TransitionCatalogValidator.Validate(Catalog(rules: rules), Known));

        Assert.Equal(CatalogViolationKind.MissingNotificationDecision, violation.Kind);
        Assert.Contains("NotificationDecision.None", violation.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("test-records.taken", false, false)]
    [InlineData(null, true, false)]
    [InlineData(" ", false, true)]
    public void A_half_made_notification_decision_is_reported(string? template, bool notifyRequester, bool notifyAssignee)
    {
        var decision = new NotificationDecision(template, notifyRequester, notifyAssignee);
        var rules = TestRecordWorkflow.Rules.Select(rule => rule.ActionKey == TestRecordActions.Take ? rule with { Notification = decision } : rule).ToList();

        Assert.Equal(CatalogViolationKind.MissingNotificationDecision, Assert.Single(TransitionCatalogValidator.Validate(Catalog(rules: rules), Known)).Kind);
    }

    [Fact]
    public void An_explicit_no_notification_decision_is_valid() =>
        Assert.False(NotificationDecision.None.NotifiesAnyone);

    [Fact]
    public void A_rule_to_an_undefined_state_is_reported()
    {
        const int undefined = 42;
        var rules = TestRecordWorkflow.Rules.Append(TestRecordWorkflow.Rules[0] with { ActionKey = "jump", ToStatus = undefined }).ToList();

        var violation = Assert.Single(TransitionCatalogValidator.Validate(Catalog(rules: rules), Known));

        Assert.Equal(CatalogViolationKind.UndefinedState, violation.Kind);
        Assert.Contains("to-status 42", violation.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void A_rule_from_an_undefined_state_is_reported()
    {
        const int undefined = 42;
        var rules = TestRecordWorkflow.Rules.Append(TestRecordWorkflow.Rules[0] with { ActionKey = "jump", FromStatus = undefined }).ToList();

        var violation = Assert.Single(TransitionCatalogValidator.Validate(Catalog(rules: rules), Known));

        Assert.Equal(CatalogViolationKind.UndefinedState, violation.Kind);
        Assert.Contains("from-status 42", violation.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void A_status_value_defined_twice_is_reported()
    {
        var statuses = TestRecordWorkflow.Statuses.Append(TestRecordWorkflow.Statuses[1] with { Key = "again" }).ToList();

        Assert.Equal(CatalogViolationKind.UndefinedState, Assert.Single(TransitionCatalogValidator.Validate(Catalog(statuses: statuses), Known)).Kind);
    }

    [Fact]
    public void A_catalog_without_an_initial_state_is_reported_and_reachability_is_not_guessed()
    {
        var statuses = TestRecordWorkflow.Statuses.Select(status => status with { IsInitial = false }).ToList();

        var violation = Assert.Single(TransitionCatalogValidator.Validate(Catalog(statuses: statuses), Known));

        Assert.Equal(CatalogViolationKind.NoInitialState, violation.Kind);
        Assert.Contains("found 0", violation.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void A_catalog_with_two_initial_states_is_reported()
    {
        var statuses = TestRecordWorkflow.Statuses.Select(status => status.Status == TestRecordStatuses.Submitted ? status with { IsInitial = true } : status).ToList();

        Assert.Contains(TransitionCatalogValidator.Validate(Catalog(statuses: statuses), Known), violation => violation.Kind == CatalogViolationKind.NoInitialState);
    }

    [Fact]
    public void An_action_declared_twice_from_one_status_is_reported()
    {
        var rules = TestRecordWorkflow.Rules.Append(TestRecordWorkflow.Rules[0] with { ToStatus = TestRecordStatuses.Withdrawn }).ToList();

        var violation = Assert.Single(TransitionCatalogValidator.Validate(Catalog(rules: rules), Known));

        Assert.Equal(CatalogViolationKind.DuplicateAction, violation.Kind);
    }

    [Fact]
    public void A_transition_out_of_a_terminal_state_is_reported()
    {
        var rules = TestRecordWorkflow.Rules.Append(TestRecordWorkflow.Rules[0] with { ActionKey = "reopen", FromStatus = TestRecordStatuses.Accepted }).ToList();

        Assert.Equal(CatalogViolationKind.TransitionFromTerminal, Assert.Single(TransitionCatalogValidator.Validate(Catalog(rules: rules), Known)).Kind);
    }

    [Fact]
    public void A_violation_names_its_record_type_kind_and_subject()
    {
        var violation = TransitionCatalogValidator.Validate(BrokenCatalogs.WithoutPermission, Known)[0];

        Assert.Equal(TestRecordWorkflow.RecordType, violation.RecordType);
        Assert.StartsWith("[test-record] MissingPermission take (2 -> 3): ", violation.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void The_validator_rejects_null_arguments()
    {
        Assert.Throws<ArgumentNullException>(() => TransitionCatalogValidator.Validate(null!, Known));
        Assert.Throws<ArgumentNullException>(() => TransitionCatalogValidator.Validate(TestRecordWorkflow.Catalog, null!));
    }

    private static TransitionCatalog Catalog(IReadOnlyList<WorkflowStatusDefinition>? statuses = null, IReadOnlyList<TransitionRule>? rules = null) =>
        new(TestRecordWorkflow.RecordType, statuses ?? TestRecordWorkflow.Statuses, rules ?? TestRecordWorkflow.Rules);

    private static StatusWording Blank(StatusWording wording, WorkflowAudience audience) => audience switch
    {
        WorkflowAudience.Requester => wording with { RequesterKey = string.Empty },
        WorkflowAudience.Staff => wording with { StaffKey = string.Empty },
        _ => wording with { AdminKey = string.Empty },
    };
}
