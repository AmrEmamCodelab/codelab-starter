namespace Starter.Domain.Workflow;

/// <summary>Whether a transition takes a reason from the actor. Values are sent on the wire; never renumber a member.</summary>
public enum ReasonRequirement
{
    /// <summary>No reason is asked for; a reason sent anyway is dropped.</summary>
    None = 1,

    /// <summary>The actor may give a reason.</summary>
    Optional = 2,

    /// <summary>The transition is refused without a non-blank reason.</summary>
    Required = 3,
}
