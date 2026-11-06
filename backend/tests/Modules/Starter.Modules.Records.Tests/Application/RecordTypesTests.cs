using Starter.Application.Records;
using Starter.Application.Workflow;
using Starter.Domain.Workflow;
using Starter.Modules.Records.Application;
using Starter.Modules.Records.Tests.Support;
using Starter.Workflow.Testing;

namespace Starter.Modules.Records.Tests.Application;

public sealed class RecordTypesTests
{
    [Fact]
    public void A_registration_joins_its_catalog()
    {
        var types = Types(LifecycleTestRecord.Registration);

        var type = Assert.Single(types.All);
        Assert.Same(type, types.Find(TestRecordWorkflow.RecordType));
        Assert.Same(type, types.FindByAttachableType(LifecycleTestRecord.AttachableRecordType));
        Assert.Null(types.Find("other-record"));
        Assert.Null(types.Find(null));
        Assert.Null(types.FindByAttachableType(null));
        Assert.Equal(TestRecordStatuses.Draft, type.InitialStatus.Status);
        Assert.Equal(TestRecordPermissionKeys.Create, type.CreatePermissionKey);
    }

    [Fact]
    public void The_permission_keys_cover_the_rules_view_own_queue_and_oversight_once_each()
    {
        var type = Types(LifecycleTestRecord.Registration).All[0];

        Assert.Equal(
            new[] { TestRecordPermissionKeys.Create, TestRecordPermissionKeys.Review, TestRecordPermissionKeys.Decide, LifecycleTestRecord.ViewOwn, LifecycleTestRecord.Queue, RecordsPermissionKeys.OversightView }.Order(),
            type.PermissionKeys.Order());
    }

    [Theory]
    [InlineData(TestRecordStatuses.Draft, true)]
    [InlineData(TestRecordStatuses.Returned, true)]
    [InlineData(TestRecordStatuses.Submitted, false)]
    [InlineData(TestRecordStatuses.Accepted, false)]
    public void Editable_statuses_are_the_initial_one_and_those_a_resubmit_leaves(int status, bool editable)
    {
        Assert.Equal(editable, Types(LifecycleTestRecord.Registration).All[0].IsEditable(status));
    }

    [Fact]
    public void A_status_key_falls_back_to_the_number_for_an_undefined_status()
    {
        var type = Types(LifecycleTestRecord.Registration).All[0];

        Assert.Equal("in-review", type.StatusKeyOf(TestRecordStatuses.InReview));
        Assert.Equal("42", type.StatusKeyOf(42));
    }

    [Fact]
    public void A_type_registered_twice_and_a_shared_attachable_type_are_both_reported()
    {
        var problems = RecordTypeProblems.Find([LifecycleTestRecord.Registration, LifecycleTestRecord.Registration], Catalogs());

        Assert.Contains(problems, problem => problem.Contains("record type 'test-record' is registered more than once", StringComparison.Ordinal));
        Assert.Contains(problems, problem => problem.Contains("attachable record type 'test-record-files'", StringComparison.Ordinal));
        Assert.Throws<InvalidOperationException>(() => Types(LifecycleTestRecord.Registration, LifecycleTestRecord.Registration));
    }

    [Fact]
    public void A_registration_without_a_workflow_is_reported()
    {
        var orphan = LifecycleTestRecord.Registration with { };
        var problems = RecordTypeProblems.Find([orphan], new RecordWorkflowCatalogs([]));

        Assert.Contains("has no workflow", Assert.Single(problems), StringComparison.Ordinal);
    }

    [Fact]
    public void A_workflow_without_an_initial_status_or_without_a_submit_rule_is_reported()
    {
        var noInitial = new TransitionCatalog(
            TestRecordWorkflow.RecordType,
            [.. TestRecordWorkflow.Statuses.Select(status => status with { IsInitial = false })],
            TestRecordWorkflow.Rules);
        var noSubmit = new TransitionCatalog(
            TestRecordWorkflow.RecordType,
            TestRecordWorkflow.Statuses,
            [.. TestRecordWorkflow.Rules.Where(rule => rule.ActionKey != TestRecordActions.Submit)]);

        Assert.Contains("exactly one initial status", Assert.Single(RecordTypeProblems.Find([LifecycleTestRecord.Registration], Catalogs(noInitial))), StringComparison.Ordinal);
        Assert.Contains("no 'submit' rule", Assert.Single(RecordTypeProblems.Find([LifecycleTestRecord.Registration], Catalogs(noSubmit))), StringComparison.Ordinal);
    }

    [Fact]
    public void Submit_and_resubmit_are_the_submit_actions()
    {
        Assert.True(RecordLifecycleActions.IsSubmit(RecordLifecycleActions.Submit));
        Assert.True(RecordLifecycleActions.IsSubmit(RecordLifecycleActions.Resubmit));
        Assert.False(RecordLifecycleActions.IsSubmit(RecordLifecycleActions.Cancel));
        Assert.False(RecordLifecycleActions.IsSubmit(null));
    }

    private static RecordTypes Types(params RecordTypeRegistration[] registrations) =>
        new([new FakeRegistrations(registrations)], Catalogs());

    private static RecordWorkflowCatalogs Catalogs(TransitionCatalog? catalog = null) =>
        new([new CatalogDefinition(catalog ?? LifecycleTestRecord.Catalog)]);

    private sealed class CatalogDefinition(TransitionCatalog catalog) : IRecordWorkflowDefinition
    {
        public string RecordType => catalog.RecordType;

        public TransitionCatalog Catalog => catalog;
    }
}
