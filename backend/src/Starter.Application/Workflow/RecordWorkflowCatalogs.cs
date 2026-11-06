using Starter.Domain.Workflow;

namespace Starter.Application.Workflow;

/// <summary>
/// The registry over every registered <see cref="IRecordWorkflowDefinition"/>. Record type keys are compared
/// ordinally. Two definitions of one record type are a configuration fault the startup check reports first.
/// </summary>
public sealed class RecordWorkflowCatalogs : IRecordWorkflowCatalogs
{
    private readonly Dictionary<string, TransitionCatalog> _byRecordType = new(StringComparer.Ordinal);

    /// <summary>Indexes <paramref name="definitions"/>.</summary>
    /// <exception cref="InvalidOperationException">Two definitions declare the same record type.</exception>
    public RecordWorkflowCatalogs(IEnumerable<IRecordWorkflowDefinition> definitions)
    {
        ArgumentNullException.ThrowIfNull(definitions);
        foreach (var definition in definitions)
        {
            if (!_byRecordType.TryAdd(definition.RecordType, definition.Catalog))
            {
                throw new InvalidOperationException($"Record type '{definition.RecordType}' has more than one workflow definition.");
            }
        }

        All = [.. _byRecordType.OrderBy(pair => pair.Key, StringComparer.Ordinal).Select(pair => pair.Value)];
    }

    /// <inheritdoc />
    public IReadOnlyList<TransitionCatalog> All { get; }

    /// <inheritdoc />
    public TransitionCatalog? Find(string recordType) => _byRecordType.GetValueOrDefault(recordType);
}
