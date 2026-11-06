using Starter.Domain.Workflow;

namespace Starter.Application.Workflow;

/// <summary>Every registered workflow catalog, by record type.</summary>
public interface IRecordWorkflowCatalogs
{
    /// <summary>Every catalog, ordered by record type.</summary>
    IReadOnlyList<TransitionCatalog> All { get; }

    /// <summary>The catalog of <paramref name="recordType"/>, or null when no definition declares it.</summary>
    TransitionCatalog? Find(string recordType);
}
