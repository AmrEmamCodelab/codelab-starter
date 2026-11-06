using Starter.Application.Workflow;
using Starter.Domain.Workflow;
using Starter.Workflow.Testing;

namespace Starter.Application.Tests.Workflow;

/// <summary>The <see cref="IRecordWorkflow{TRecord}"/> facade, the catalog registry and the cross-definition validation.</summary>
public sealed class RecordWorkflowFacadeTests
{
    private const long OwnerId = 7;
    private const long StaffId = 8;

    private static readonly RecordWorkflow<TestRecord> Workflow = new(
        new RecordWorkflowCatalogs([new TestRecordWorkflowDefinition()]),
        new TestRecordReader());

    [Fact]
    public void The_owner_is_offered_the_requester_actions_of_their_draft()
    {
        var actor = new WorkflowActor(OwnerId, TransitionSide.Requester, Keys(TestRecordPermissionKeys.Create));

        var actions = Workflow.AllowedActions(new TestRecord(TestRecordWorkflow.RecordType, TestRecordStatuses.Draft, OwnerId), actor);

        Assert.Equal([TestRecordActions.Submit, TestRecordActions.Withdraw], actions.Select(action => action.ActionKey));
    }

    [Fact]
    public void Another_user_with_the_same_permission_is_not_the_owner()
    {
        var actor = new WorkflowActor(StaffId, TransitionSide.Requester, Keys(TestRecordPermissionKeys.Create));
        var record = new TestRecord(TestRecordWorkflow.RecordType, TestRecordStatuses.Draft, OwnerId);

        Assert.Empty(Workflow.AllowedActions(record, actor));
        Assert.Equal(WorkflowRefusal.Forbidden, Assert.IsType<TransitionDecision.Refused>(Workflow.Decide(record, TestRecordActions.Submit, reason: null, actor)).Refusal);
    }

    [Fact]
    public void Staff_decide_through_the_facade()
    {
        var actor = new WorkflowActor(StaffId, TransitionSide.Staff, Keys(TestRecordPermissionKeys.Decide));

        var decision = Workflow.Decide(new TestRecord(TestRecordWorkflow.RecordType, TestRecordStatuses.InReview, OwnerId), TestRecordActions.Decline, "duplicate", actor);

        Assert.Equal(TestRecordStatuses.Declined, Assert.IsType<TransitionDecision.Allowed>(decision).Rule.ToStatus);
    }

    [Fact]
    public void A_record_type_without_a_workflow_is_a_configuration_fault()
    {
        var actor = new WorkflowActor(OwnerId, TransitionSide.Requester, Keys());
        var record = new TestRecord("unregistered", TestRecordStatuses.Draft, OwnerId);

        var fault = Assert.Throws<InvalidOperationException>(() => Workflow.AllowedActions(record, actor));
        Assert.Contains("unregistered", fault.Message, StringComparison.Ordinal);
        Assert.Throws<InvalidOperationException>(() => Workflow.Decide(record, TestRecordActions.Submit, reason: null, actor));
    }

    [Fact]
    public void The_registry_finds_catalogs_by_exact_record_type()
    {
        var catalogs = new RecordWorkflowCatalogs([new TestRecordWorkflowDefinition()]);

        Assert.Same(TestRecordWorkflow.Catalog, catalogs.Find(TestRecordWorkflow.RecordType));
        Assert.Null(catalogs.Find("TEST-RECORD"));
        Assert.Same(TestRecordWorkflow.Catalog, Assert.Single(catalogs.All));
    }

    [Fact]
    public void The_registry_holds_zero_catalogs() => Assert.Empty(new RecordWorkflowCatalogs([]).All);

    [Fact]
    public void The_registry_refuses_two_definitions_of_one_record_type() =>
        Assert.Throws<InvalidOperationException>(() => new RecordWorkflowCatalogs([new TestRecordWorkflowDefinition(), new TestRecordWorkflowDefinition()]));

    [Fact]
    public void Validating_every_definition_passes_for_valid_ones_and_for_none()
    {
        Assert.Empty(RecordWorkflowValidation.ValidateAll([new TestRecordWorkflowDefinition()], TestRecordPermissionKeys.All));
        Assert.Empty(RecordWorkflowValidation.ValidateAll([], TestRecordPermissionKeys.All));
    }

    [Fact]
    public void Validating_every_definition_reports_a_duplicated_record_type_once()
    {
        var violations = RecordWorkflowValidation.ValidateAll([new TestRecordWorkflowDefinition(), new TestRecordWorkflowDefinition()], TestRecordPermissionKeys.All);

        Assert.Equal(CatalogViolationKind.RecordTypeConflict, Assert.Single(violations).Kind);
    }

    [Fact]
    public void Validating_every_definition_reports_a_record_type_that_differs_from_its_catalog()
    {
        var violations = RecordWorkflowValidation.ValidateAll([new MismatchedDefinition()], TestRecordPermissionKeys.All);

        Assert.Equal(CatalogViolationKind.RecordTypeConflict, Assert.Single(violations).Kind);
    }

    [Fact]
    public void Validating_every_definition_collects_the_violations_of_each_catalog()
    {
        var violations = RecordWorkflowValidation.ValidateAll([new BrokenDefinition()], BrokenCatalogs.KnownPermissions);

        Assert.Equal(3, violations.Count);
    }

    private static HashSet<string> Keys(params string[] keys) => keys.ToHashSet(StringComparer.Ordinal);

    private sealed record TestRecord(string RecordType, int Status, long OwnerUserId);

    private sealed class TestRecordReader : IWorkflowRecordReader<TestRecord>
    {
        public string RecordTypeOf(TestRecord record) => record.RecordType;

        public int StatusOf(TestRecord record) => record.Status;

        public long OwnerUserIdOf(TestRecord record) => record.OwnerUserId;
    }

    private sealed class MismatchedDefinition : IRecordWorkflowDefinition
    {
        public string RecordType => "other-record";

        public TransitionCatalog Catalog => TestRecordWorkflow.Catalog;
    }

    private sealed class BrokenDefinition : IRecordWorkflowDefinition
    {
        public string RecordType => TestRecordWorkflow.RecordType;

        public TransitionCatalog Catalog => BrokenCatalogs.Combined;
    }
}
