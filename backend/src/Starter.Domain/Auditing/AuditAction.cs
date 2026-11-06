namespace Starter.Domain.Auditing;

/// <summary>What happened to an audited row (stored as int; never reorder or renumber).</summary>
public enum AuditAction
{
    /// <summary>The row was inserted.</summary>
    Added = 1,

    /// <summary>At least one audited property of the row changed.</summary>
    Modified = 2,

    /// <summary>The row was deleted.</summary>
    Deleted = 3,
}
