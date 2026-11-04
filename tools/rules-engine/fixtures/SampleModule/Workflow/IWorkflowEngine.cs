namespace Starter.Modules.Sample.Workflow;

/// <summary>Port through which sample records change status.</summary>
public interface IWorkflowEngine
{
    /// <summary>Moves a record to <paramref name="target"/> when the transition catalog allows it.</summary>
    Task<SampleStatus> AdvanceAsync(SampleStatus current, SampleStatus target, CancellationToken cancellationToken);
}
