namespace Starter.Domain.Workflow;

/// <summary>One status of a record type's workflow.</summary>
/// <param name="Status">The stored value: a constant of the record type's statuses class, unique in its catalog.</param>
/// <param name="Key">The kebab key (<c>under-review</c>) used in i18n keys and on the wire next to the value.</param>
/// <param name="IsInitial">True for the one status a new record starts in.</param>
/// <param name="IsTerminal">True when the record is closed here: no transition leaves a terminal status.</param>
/// <param name="Wording">The audience-specific i18n keys.</param>
public sealed record WorkflowStatusDefinition(int Status, string Key, bool IsInitial, bool IsTerminal, StatusWording Wording);
