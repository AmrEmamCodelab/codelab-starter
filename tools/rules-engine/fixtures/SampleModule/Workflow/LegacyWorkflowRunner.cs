namespace Starter.Modules.Sample.Workflow;

/// <summary>Old runner kept for the fixture: module.rules.yaml bans constructing it.</summary>
public sealed class LegacyWorkflowRunner
{
    /// <summary>The status set by the last <see cref="Run"/> call.</summary>
    public SampleStatus LastStatus { get; private set; }

    /// <summary>Sets the target status without consulting the transition catalog.</summary>
    public SampleStatus Run(SampleStatus target)
    {
        LastStatus = target;
        return LastStatus;
    }
}
