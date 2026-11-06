namespace Starter.Domain.Workflow;

/// <summary>What is wrong with a <see cref="TransitionCatalog"/>. Every kind fails the host's start.</summary>
public enum CatalogViolationKind
{
    /// <summary>A rule names a status the catalog does not define, or a status is defined twice.</summary>
    UndefinedState = 1,

    /// <summary>No chain of rules leads from the initial status to this status.</summary>
    UnreachableState = 2,

    /// <summary>A rule has no permission key.</summary>
    MissingPermission = 3,

    /// <summary>A rule's permission key is not in the permission catalog.</summary>
    UnknownPermission = 4,

    /// <summary>A reason-required rule names no reason field.</summary>
    ReasonFieldMissing = 5,

    /// <summary>A status lacks the wording of an audience, or a rule lacks its label key.</summary>
    MissingWording = 6,

    /// <summary>A rule has no notification decision, or a decision that names a template without recipients (or the reverse).</summary>
    MissingNotificationDecision = 7,

    /// <summary>The catalog has no initial status, or more than one.</summary>
    NoInitialState = 8,

    /// <summary>Two rules share an action key and a from-status, so a request cannot choose between them.</summary>
    DuplicateAction = 9,

    /// <summary>A rule leaves a terminal status (terminal statuses offer no actions).</summary>
    TransitionFromTerminal = 10,

    /// <summary>Two registered workflow definitions declare the same record type, or a definition's record type differs from its catalog's.</summary>
    RecordTypeConflict = 11,
}
