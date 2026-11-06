using Starter.Application.Workflow;
using Starter.Domain.Workflow;

namespace Starter.Contract.Tests.Workflow;

/// <summary>Unit tests of <see cref="WorkflowConformance"/> over a synthetic catalog (no host, no record-type module).</summary>
public sealed class WorkflowConformanceTests
{
    private const string Permission = "conformance-probes.act";
    private const int Open = 1;
    private const int Closed = 2;
    private static readonly IReadOnlySet<string> Known = new HashSet<string>(StringComparer.Ordinal) { Permission };

    [Fact]
    public void ZeroCatalogsConform() => Assert.Empty(WorkflowConformance.Check([], [], Known));

    [Fact]
    public void AConformingCatalogPasses() =>
        Assert.Empty(WorkflowConformance.Check([new ProbeDefinition(Rule())], [typeof(ProbeDefinition)], Known));

    [Fact]
    public void ATransitionWithoutAPermissionFails() =>
        Assert.Contains("permission", Single(Rule() with { PermissionKey = " " }), StringComparison.Ordinal);

    [Fact]
    public void ATransitionWithAnUnknownPermissionFails() =>
        Assert.Contains("not in the permission catalog", Single(Rule() with { PermissionKey = "conformance-probes.other" }), StringComparison.Ordinal);

    [Fact]
    public void ATransitionWithoutANotificationDecisionFails() =>
        Assert.Contains("notification template decision", Single(Rule() with { Notification = null }), StringComparison.Ordinal);

    [Fact]
    public void ATransitionWithoutALabelFails() =>
        Assert.Contains("label", Single(Rule() with { LabelKey = string.Empty }), StringComparison.Ordinal);

    [Fact]
    public void ATransitionToAStatusWithoutAnAudienceWordingFails()
    {
        var statuses = new[] { Status(Open, isInitial: true, isTerminal: false), Status(Closed, isInitial: false, isTerminal: true) with { Wording = new StatusWording("r", "s", " ") } };
        var definition = new ProbeDefinition(new TransitionCatalog(ProbeDefinition.Type, statuses, [Rule()]));

        var breach = Assert.Single(WorkflowConformance.Check([definition], [typeof(ProbeDefinition)], Known));

        Assert.Contains(nameof(WorkflowAudience.Admin), breach, StringComparison.Ordinal);
    }

    [Fact]
    public void ADeclaredButUnregisteredDefinitionFails() =>
        Assert.Contains("AddRecordWorkflow<ProbeDefinition>", Assert.Single(WorkflowConformance.Check([], [typeof(ProbeDefinition)], Known)), StringComparison.Ordinal);

    [Fact]
    public void DefinitionTypesAreDiscoveredInAnAssembly() =>
        Assert.Contains(typeof(ProbeDefinition), WorkflowConformance.DefinitionTypesIn([typeof(ProbeDefinition).Assembly]));

    private static string Single(TransitionRule rule) =>
        Assert.Single(WorkflowConformance.Check([new ProbeDefinition(rule)], [typeof(ProbeDefinition)], Known));

    private static TransitionRule Rule() => new(
        "close",
        FromStatus: Open,
        ToStatus: Closed,
        Permission,
        TransitionSide.Staff,
        ReasonRequirement.None,
        ReasonFieldKey: null,
        LabelKey: "conformanceProbes.action.close",
        NotificationDecision.None,
        RequiresOwnership: false);

    private static WorkflowStatusDefinition Status(int status, bool isInitial, bool isTerminal) =>
        new(status, $"s{status}", isInitial, isTerminal, new StatusWording($"r{status}", $"s{status}", $"a{status}"));

    private sealed class ProbeDefinition : IRecordWorkflowDefinition
    {
        public const string Type = "conformance-probe";

        public ProbeDefinition(TransitionRule rule)
            : this(new TransitionCatalog(Type, [Status(Open, isInitial: true, isTerminal: false), Status(Closed, isInitial: false, isTerminal: true)], [rule]))
        {
        }

        public ProbeDefinition(TransitionCatalog catalog) => Catalog = catalog;

        public string RecordType => Type;

        public TransitionCatalog Catalog { get; }
    }
}
