namespace Starter.Domain.Workflow;

/// <summary>
/// Which side of the counter takes a transition: the requester screens, the staff screens or a background job. The
/// allowed actions offered to a screen are those of its side. Values are stored and sent on the wire.
/// </summary>
public enum TransitionSide
{
    /// <summary>The record's requester (usually also its owner).</summary>
    Requester = 1,

    /// <summary>Staff working the record.</summary>
    Staff = 2,

    /// <summary>A background job acting as the SYSTEM actor.</summary>
    System = 3,
}
