namespace Starter.Domain.Workflow;

/// <summary>
/// One allowed move of a record type: <paramref name="ActionKey"/> takes a record from <paramref name="FromStatus"/>
/// to <paramref name="ToStatus"/> for an actor who holds <paramref name="PermissionKey"/> (and owns the record when
/// <paramref name="RequiresOwnership"/>). An action legal from two statuses is two rules with the same key.
/// </summary>
/// <param name="ActionKey">The kebab action key (<c>start-review</c>), a constant of the record type's actions class.</param>
/// <param name="FromStatus">The status the action is legal from.</param>
/// <param name="ToStatus">The status the record lands on.</param>
/// <param name="PermissionKey">The permission the actor must hold, a key of the permission catalog.</param>
/// <param name="Side">Which screens offer the action.</param>
/// <param name="Reason">Whether the actor gives a reason.</param>
/// <param name="ReasonFieldKey">The form field the reason is entered in (required when the reason is required).</param>
/// <param name="LabelKey">The i18n key of the action's button label.</param>
/// <param name="Notification">What the transition announces; never null in a valid catalog.</param>
/// <param name="RequiresOwnership">True when only the record's owner may take the action.</param>
public sealed record TransitionRule(
    string ActionKey,
    int FromStatus,
    int ToStatus,
    string PermissionKey,
    TransitionSide Side,
    ReasonRequirement Reason,
    string? ReasonFieldKey,
    string LabelKey,
    NotificationDecision? Notification,
    bool RequiresOwnership)
{
    /// <summary>A short description for violations and test failures: <c>action (from -> to)</c>.</summary>
    public string Describe() => $"{ActionKey} ({FromStatus} -> {ToStatus})";
}
