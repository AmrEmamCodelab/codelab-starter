using Starter.Domain.Workflow;

namespace Starter.Workflow.Testing;

/// <summary>
/// Deliberately broken variants of <see cref="TestRecordWorkflow.Catalog"/>, one defect each (plus
/// <see cref="Combined"/>), for validator tests and the "a broken catalog fails the boot" host test. Validate them
/// against <see cref="KnownPermissions"/>.
/// </summary>
public static class BrokenCatalogs
{
    /// <summary>The permission catalog the broken catalogs are validated against.</summary>
    public static IReadOnlySet<string> KnownPermissions => TestRecordPermissionKeys.All;

    /// <summary>Defines <see cref="TestRecordStatuses.Orphan"/>, which no rule reaches.</summary>
    public static TransitionCatalog Unreachable { get; } = Build(withOrphan: true, withoutPermission: false, reasonWithoutField: false);

    /// <summary>The <see cref="TestRecordActions.Take"/> rule has no permission key.</summary>
    public static TransitionCatalog WithoutPermission { get; } = Build(withOrphan: false, withoutPermission: true, reasonWithoutField: false);

    /// <summary>The reason-required <see cref="TestRecordActions.Return"/> rule names no reason field.</summary>
    public static TransitionCatalog ReasonWithoutField { get; } = Build(withOrphan: false, withoutPermission: false, reasonWithoutField: true);

    /// <summary>All three defects above in one catalog (the workflow-conformance exit criterion).</summary>
    public static TransitionCatalog Combined { get; } = Build(withOrphan: true, withoutPermission: true, reasonWithoutField: true);

    private static TransitionCatalog Build(bool withOrphan, bool withoutPermission, bool reasonWithoutField)
    {
        IReadOnlyList<WorkflowStatusDefinition> statuses = withOrphan
            ? [.. TestRecordWorkflow.Statuses, TestRecordWorkflow.Status(TestRecordStatuses.Orphan, "orphan", isInitial: false, isTerminal: false)]
            : TestRecordWorkflow.Statuses;
        var rules = TestRecordWorkflow.Rules
            .Select(rule => withoutPermission && rule.ActionKey == TestRecordActions.Take ? rule with { PermissionKey = string.Empty } : rule)
            .Select(rule => reasonWithoutField && rule.ActionKey == TestRecordActions.Return ? rule with { ReasonFieldKey = null } : rule)
            .ToList();
        return new TransitionCatalog(TestRecordWorkflow.RecordType, statuses, rules);
    }
}
