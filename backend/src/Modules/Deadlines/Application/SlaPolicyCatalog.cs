using Starter.Application.Records;
using Starter.Application.Workflow;

namespace Starter.Modules.Deadlines.Application;

/// <summary>What is wrong with an SLA definition (reported at startup; sent nowhere).</summary>
public enum SlaPolicyViolationKind
{
    /// <summary>Two definitions name the same record type and status.</summary>
    DuplicateStatus = 1,

    /// <summary>The record type has a workflow, and it does not define the status.</summary>
    UndefinedStatus = 2,

    /// <summary>The status is terminal: a record never leaves it, so the deadline could never close.</summary>
    TerminalStatus = 3,
}

/// <summary>One problem of the SLA definitions.</summary>
/// <param name="RecordType">The record type key.</param>
/// <param name="Status">The status value.</param>
/// <param name="Kind">What is wrong.</param>
public sealed record SlaPolicyViolation(string RecordType, int Status, SlaPolicyViolationKind Kind)
{
    /// <inheritdoc />
    public override string ToString() => $"{Kind}: SLA of record type '{RecordType}', status {Status}";
}

/// <summary>
/// Every SLA definition of the host, read once from every registered <see cref="ISlaPolicySource"/> and indexed by
/// record type and status (ordinal). A duplicated definition keeps the first one; <see cref="FindViolations"/> reports
/// it, and the startup check refuses to start on any violation.
/// </summary>
public sealed class SlaPolicyCatalog
{
    private readonly Dictionary<(string RecordType, int Status), SlaDefinition> _byStatus = [];

    /// <summary>Reads every source.</summary>
    public SlaPolicyCatalog(IEnumerable<ISlaPolicySource> sources)
    {
        ArgumentNullException.ThrowIfNull(sources);
        All = [.. sources.SelectMany(source => source.Definitions)];
        foreach (var definition in All)
        {
            _byStatus.TryAdd((definition.RecordType, definition.Status), definition);
        }
    }

    /// <summary>Every definition, in source order (duplicates included).</summary>
    public IReadOnlyList<SlaDefinition> All { get; }

    /// <summary>The SLA of <paramref name="status"/> of <paramref name="recordType"/>, or null when the status has none.</summary>
    public SlaDefinition? Find(string recordType, int status) => _byStatus.GetValueOrDefault((recordType, status));

    /// <summary>
    /// Every problem of the definitions: a duplicated record type and status, and, for a record type that has a workflow
    /// in <paramref name="workflows"/>, a status the workflow does not define or one that is terminal.
    /// </summary>
    public IReadOnlyList<SlaPolicyViolation> FindViolations(IRecordWorkflowCatalogs workflows)
    {
        ArgumentNullException.ThrowIfNull(workflows);
        var violations = All
            .GroupBy(definition => (definition.RecordType, definition.Status))
            .Where(group => group.Skip(1).Any())
            .Select(group => new SlaPolicyViolation(group.Key.RecordType, group.Key.Status, SlaPolicyViolationKind.DuplicateStatus))
            .ToList();
        foreach (var definition in _byStatus.Values)
        {
            if (WorkflowViolation(definition, workflows) is { } kind)
            {
                violations.Add(new SlaPolicyViolation(definition.RecordType, definition.Status, kind));
            }
        }

        return violations;
    }

    private static SlaPolicyViolationKind? WorkflowViolation(SlaDefinition definition, IRecordWorkflowCatalogs workflows)
    {
        if (workflows.Find(definition.RecordType) is not { } catalog)
        {
            return null;
        }

        if (catalog.FindStatus(definition.Status) is not { } status)
        {
            return SlaPolicyViolationKind.UndefinedStatus;
        }

        return status.IsTerminal ? SlaPolicyViolationKind.TerminalStatus : null;
    }
}
