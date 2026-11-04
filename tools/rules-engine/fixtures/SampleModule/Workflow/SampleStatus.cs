namespace Starter.Modules.Sample.Workflow;

/// <summary>Sample record status; stored as int, every member has an explicit value.</summary>
public enum SampleStatus
{
    /// <summary>Not submitted yet.</summary>
    Draft = 1,

    /// <summary>Submitted for review.</summary>
    Submitted = 2,
}
