namespace Starter.Modules.Assignments.Domain;

/// <summary>
/// Which slice of a work queue a request lists. Travels as a lower-case word on the wire (<c>view=unassigned</c>, see
/// <c>QueueViews</c>), never as this value; stored nowhere.
/// </summary>
public enum QueueView
{
    /// <summary>Open records nobody holds.</summary>
    Unassigned = 1,

    /// <summary>Open records the caller holds.</summary>
    Mine = 2,

    /// <summary>Every open record, held or not.</summary>
    All = 3,
}
