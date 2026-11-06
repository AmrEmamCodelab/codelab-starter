namespace Starter.Domain.Workflow;

/// <summary>
/// Who reads a status: each audience has its own wording for the same status (the requester sees "With the team",
/// staff see "Under review"). Values are stored and sent on the wire; never renumber a member.
/// </summary>
public enum WorkflowAudience
{
    /// <summary>The person who filed the record.</summary>
    Requester = 1,

    /// <summary>The staff who work the record.</summary>
    Staff = 2,

    /// <summary>Administrators with the read-only oversight view.</summary>
    Admin = 3,
}
