namespace Starter.Domain.Workflow;

/// <summary>
/// What a transition announces. Every rule carries a decision, also when it notifies nobody
/// (<see cref="None"/>): a rule whose decision is null is a catalog violation, so "no notification" is always a
/// choice someone made, never an omission.
/// </summary>
/// <param name="TemplateKey">The notification template key, or null exactly when nobody is notified.</param>
/// <param name="NotifyRequester">Notify the record's requester.</param>
/// <param name="NotifyAssignee">Notify the record's current assignee, when it has one.</param>
public sealed record NotificationDecision(string? TemplateKey, bool NotifyRequester, bool NotifyAssignee)
{
    /// <summary>The explicit decision to notify nobody.</summary>
    public static NotificationDecision None { get; } = new(TemplateKey: null, NotifyRequester: false, NotifyAssignee: false);

    /// <summary>True when the decision notifies at least one recipient.</summary>
    public bool NotifiesAnyone => NotifyRequester || NotifyAssignee;
}
