namespace Starter.Domain.Workflow;

/// <summary>
/// The whole workflow of one record type as data: its statuses and the rules that move between them. The catalog
/// does not validate itself; <see cref="TransitionCatalogValidator"/> reports every problem and the host refuses to
/// start on any. Lookups tolerate an invalid catalog (the first definition of a duplicated status wins).
/// </summary>
public sealed class TransitionCatalog
{
    private readonly Dictionary<int, WorkflowStatusDefinition> _byStatus = [];

    /// <summary>Creates the catalog.</summary>
    /// <param name="recordType">The record type key (kebab case), unique across the host.</param>
    /// <param name="statuses">Every status of the record type.</param>
    /// <param name="rules">Every transition, in the order screens list the actions.</param>
    public TransitionCatalog(string recordType, IReadOnlyList<WorkflowStatusDefinition> statuses, IReadOnlyList<TransitionRule> rules)
    {
        ArgumentNullException.ThrowIfNull(recordType);
        ArgumentNullException.ThrowIfNull(statuses);
        ArgumentNullException.ThrowIfNull(rules);
        RecordType = recordType;
        Statuses = statuses;
        Rules = rules;
        foreach (var status in statuses)
        {
            _byStatus.TryAdd(status.Status, status);
        }
    }

    /// <summary>The record type key.</summary>
    public string RecordType { get; }

    /// <summary>Every status.</summary>
    public IReadOnlyList<WorkflowStatusDefinition> Statuses { get; }

    /// <summary>Every transition rule.</summary>
    public IReadOnlyList<TransitionRule> Rules { get; }

    /// <summary>The definition of <paramref name="status"/>, or null when the catalog does not define it.</summary>
    public WorkflowStatusDefinition? FindStatus(int status) => _byStatus.GetValueOrDefault(status);

    /// <summary>True when <paramref name="status"/> is defined and terminal.</summary>
    public bool IsTerminal(int status) => FindStatus(status)?.IsTerminal == true;

    /// <summary>The rules legal from <paramref name="status"/>, in catalog order.</summary>
    public IEnumerable<TransitionRule> RulesFrom(int status) => Rules.Where(rule => rule.FromStatus == status);
}
