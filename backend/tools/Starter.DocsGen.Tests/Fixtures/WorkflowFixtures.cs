using Starter.Application.Workflow;
using Starter.DocsGen.Collection;
using Starter.Domain.Workflow;

namespace Starter.DocsGen.Tests.Fixtures;

/// <summary>
/// A test-only workflow (<c>fixture-record</c>): Draft, Filed and two terminal statuses, with one action legal from two
/// statuses, one required reason and each kind of notification decision. Only DocsGen tests use it.
/// </summary>
internal static class FixtureRecordWorkflow
{
    public const string RecordType = "fixture-record";
    public const int Draft = 1;
    public const int Filed = 2;
    public const int Closed = 3;
    public const int Dropped = 4;
    public const string FileAction = "file";
    public const string CloseAction = "close";
    public const string DropAction = "drop";
    public const string FilePermission = "fixture-records.file";
    public const string DecidePermission = "fixture-records.decide";
    public const string ReasonField = "reason";

    public static TransitionCatalog Catalog { get; } = new(
        RecordType,
        [
            Status(Draft, "draft", isInitial: true, isTerminal: false),
            Status(Filed, "filed", isInitial: false, isTerminal: false),
            Status(Closed, "closed", isInitial: false, isTerminal: true),
            Status(Dropped, "dropped", isInitial: false, isTerminal: true),
        ],
        [
            Rule(FileAction, Draft, Filed, FilePermission, TransitionSide.Requester)
                with { Notification = new NotificationDecision("fixture-records.filed", NotifyRequester: false, NotifyAssignee: true) },
            Rule(CloseAction, Filed, Closed, DecidePermission, TransitionSide.Staff)
                with { Reason = ReasonRequirement.Required, ReasonFieldKey = ReasonField, Notification = new NotificationDecision("fixture-records.closed", NotifyRequester: true, NotifyAssignee: true) },
            Rule(DropAction, Draft, Dropped, FilePermission, TransitionSide.Requester)
                with { Reason = ReasonRequirement.Optional, ReasonFieldKey = ReasonField },
            Rule(DropAction, Filed, Dropped, FilePermission, TransitionSide.Requester)
                with { Reason = ReasonRequirement.Optional, ReasonFieldKey = ReasonField, Notification = null },
        ]);

    public static WorkflowDoc Doc() =>
        new(RecordType, "Fixtures", typeof(FixtureRecordWorkflowDefinition).FullName!, Catalog);

    public static string WordingKey(string key, string audience) => $"fixtureRecords.status.{key}.{audience}";

    private static WorkflowStatusDefinition Status(int status, string key, bool isInitial, bool isTerminal) => new(
        status,
        key,
        isInitial,
        isTerminal,
        new StatusWording(
            RequesterKey: WordingKey(key, "requester"),
            StaffKey: WordingKey(key, "staff"),
            AdminKey: WordingKey(key, "admin")));

    /// <summary>A rule without a reason that notifies nobody; callers adjust it with <c>with</c>.</summary>
    private static TransitionRule Rule(string action, int from, int to, string permission, TransitionSide side) => new(
        action,
        FromStatus: from,
        ToStatus: to,
        permission,
        side,
        ReasonRequirement.None,
        ReasonFieldKey: null,
        LabelKey: $"fixtureRecords.action.{action}",
        NotificationDecision.None,
        RequiresOwnership: side == TransitionSide.Requester);
}

/// <summary>The fixture workflow as a definition (declared in this test assembly, so the reflection cross-check sees it).</summary>
internal sealed class FixtureRecordWorkflowDefinition : IRecordWorkflowDefinition
{
    public string RecordType => FixtureRecordWorkflow.RecordType;

    public TransitionCatalog Catalog => FixtureRecordWorkflow.Catalog;
}

/// <summary>A second, minimal definition: one initial terminal status and no rule (sort order and empty tables).</summary>
internal sealed class AlphaNoteWorkflowDefinition : IRecordWorkflowDefinition
{
    public const string Type = "alpha-note";

    public string RecordType => Type;

    public TransitionCatalog Catalog { get; } = new(
        Type,
        [new WorkflowStatusDefinition(1, "open", IsInitial: true, IsTerminal: true, new StatusWording("a.r", "a.s", "a.a"))],
        []);
}

/// <summary>A definition with whatever record type a test gives it (not a declared fixture: abstract base excluded).</summary>
internal abstract class ConfigurableWorkflowDefinitionBase : IRecordWorkflowDefinition
{
    public abstract string RecordType { get; }

    public TransitionCatalog Catalog => new(RecordType, [], []);
}

/// <summary>A definition with whatever record type a test gives it (bad keys, duplicates).</summary>
internal sealed class NamedWorkflowDefinition(string recordType) : ConfigurableWorkflowDefinitionBase
{
    public override string RecordType => recordType;
}
