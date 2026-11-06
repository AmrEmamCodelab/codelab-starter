using Starter.Domain.Workflow;

namespace Starter.Application.Workflow;

/// <summary>
/// A record type's workflow, declared by the module that owns the record type and registered with
/// <c>services.AddRecordWorkflow&lt;TDefinition&gt;()</c> (Starter.Infrastructure.Workflow). The host validates every
/// registered catalog at startup and refuses to start on any violation.
/// </summary>
public interface IRecordWorkflowDefinition
{
    /// <summary>The record type key; equal to <see cref="TransitionCatalog.RecordType"/>.</summary>
    string RecordType { get; }

    /// <summary>The statuses and transition rules. Return the same instance on every read.</summary>
    TransitionCatalog Catalog { get; }
}
