using Starter.Domain.Workflow;

namespace Starter.Application.Workflow;

/// <summary>
/// The generic workflow engine: pure functions over a <see cref="TransitionCatalog"/>. Allowed actions are derived
/// from the rules, the caller's permissions and ownership (there is no hand-written status matrix), and
/// <see cref="Decide"/> applies the same rules to one requested action. A terminal or undefined status offers nothing.
/// </summary>
/// <remarks>
/// Segregation of duties: a <see cref="TransitionSide.Staff"/> rule is never permitted to the record's owner, whatever
/// permissions the owner holds, so a requester who is also staff cannot review or decide their own record.
/// </remarks>
public static class RecordWorkflowEngine
{
    /// <summary>The actions of <paramref name="side"/> the caller may take from <paramref name="currentStatus"/>, in catalog order.</summary>
    /// <param name="catalog">The record type's catalog.</param>
    /// <param name="currentStatus">The record's status.</param>
    /// <param name="hasPermission">True when the caller holds a permission key.</param>
    /// <param name="isOwner">True when the caller owns the record.</param>
    /// <param name="side">The screens asking (requester, staff or a job).</param>
    public static IReadOnlyList<AllowedAction> AllowedActions(
        TransitionCatalog catalog,
        int currentStatus,
        Func<string, bool> hasPermission,
        bool isOwner,
        TransitionSide side)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        ArgumentNullException.ThrowIfNull(hasPermission);
        if (!IsOpen(catalog, currentStatus))
        {
            return [];
        }

        var standing = new WorkflowStanding(hasPermission, isOwner, side);
        return [.. catalog.RulesFrom(currentStatus)
            .Where(rule => Permits(rule, standing))
            .Select(rule => ToAllowedAction(catalog, rule))];
    }

    /// <summary>
    /// Decides one requested action. Order: unknown action (404), then the caller's right to the action at all
    /// (403: a rule of the caller's side, its permission, ownership and segregation of duties), then the current status
    /// (409), then the reason (400). A caller who may never take the action learns nothing about the record's status.
    /// </summary>
    /// <param name="catalog">The record type's catalog.</param>
    /// <param name="currentStatus">The record's status.</param>
    /// <param name="actionKey">The requested action.</param>
    /// <param name="reason">The reason the caller typed, or null.</param>
    /// <param name="standing">The caller's permissions, ownership and side.</param>
    public static TransitionDecision Decide(
        TransitionCatalog catalog,
        int currentStatus,
        string actionKey,
        string? reason,
        WorkflowStanding standing)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        ArgumentNullException.ThrowIfNull(standing);
        var rules = catalog.Rules.Where(rule => string.Equals(rule.ActionKey, actionKey, StringComparison.Ordinal)).ToList();
        if (rules.Count == 0)
        {
            return Refuse(WorkflowRefusal.ActionUnknown);
        }

        var permitted = rules.Where(rule => Permits(rule, standing)).ToList();
        if (permitted.Count == 0)
        {
            return Refuse(WorkflowRefusal.Forbidden);
        }

        var rule = IsOpen(catalog, currentStatus) ? permitted.Find(candidate => candidate.FromStatus == currentStatus) : null;
        return rule is null ? Refuse(WorkflowRefusal.NotAllowedFromStatus) : DecideReason(rule, reason);
    }

    private static TransitionDecision DecideReason(TransitionRule rule, string? reason)
    {
        var trimmed = string.IsNullOrWhiteSpace(reason) ? null : reason.Trim();
        if (rule.Reason == ReasonRequirement.None)
        {
            return new TransitionDecision.Allowed(rule, Reason: null);
        }

        if (trimmed is null && rule.Reason == ReasonRequirement.Required)
        {
            return new TransitionDecision.Refused(WorkflowRefusal.ReasonRequired, rule.ReasonFieldKey);
        }

        if (trimmed?.Length > WorkflowLimits.MaxReasonLength)
        {
            return new TransitionDecision.Refused(WorkflowRefusal.ReasonTooLong, rule.ReasonFieldKey);
        }

        return new TransitionDecision.Allowed(rule, trimmed);
    }

    private static bool IsOpen(TransitionCatalog catalog, int status) =>
        catalog.FindStatus(status) is { IsTerminal: false };

    private static bool Permits(TransitionRule rule, WorkflowStanding standing) =>
        rule.Side == standing.Side
        && !string.IsNullOrWhiteSpace(rule.PermissionKey)
        && standing.HasPermission(rule.PermissionKey)
        && (!rule.RequiresOwnership || standing.IsOwner)
        && !(rule.Side == TransitionSide.Staff && standing.IsOwner);

    private static AllowedAction ToAllowedAction(TransitionCatalog catalog, TransitionRule rule) => new(
        rule.ActionKey,
        rule.LabelKey,
        rule.Reason,
        rule.ReasonFieldKey,
        rule.ToStatus,
        ToTerminal: catalog.IsTerminal(rule.ToStatus));

    private static TransitionDecision.Refused Refuse(WorkflowRefusal refusal) => new(refusal, ReasonFieldKey: null);
}
