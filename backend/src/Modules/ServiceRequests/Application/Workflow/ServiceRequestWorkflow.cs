using Starter.Application.Workflow;
using Starter.Domain.Workflow;
using Starter.Modules.ServiceRequests.Domain;

namespace Starter.Modules.ServiceRequests.Application.Workflow;

/// <summary>
/// The sample's transition table, the only place its workflow is written down. The kernel validates it at start
/// (<c>TransitionCatalogValidator</c>; any violation fails the boot), computes the allowed actions from it and decides
/// every transition through it. Draft to Submitted to Under Review to Approved, Rejected or Returned; Returned to
/// Resubmitted to Under Review; Draft or Submitted to Cancelled. Approved, Rejected and Cancelled are terminal.
/// </summary>
public static class ServiceRequestWorkflow
{
    /// <summary>The reason field of the reason dialogs (return, reject and cancel).</summary>
    public const string ReasonField = "reason";

    private const string KeyPrefix = "serviceRequests";
    private const string StatusSegment = "status";
    private const string ActionSegment = "action";
    private const char KeySeparator = '.';

    /// <summary>The statuses with their audience wording keys.</summary>
    public static IReadOnlyList<WorkflowStatusDefinition> Statuses { get; } =
    [
        Status(ServiceRequestStatuses.Draft, ServiceRequestStatusKeys.Draft, StatusKind.Initial),
        Status(ServiceRequestStatuses.Submitted, ServiceRequestStatusKeys.Submitted, StatusKind.Open),
        Status(ServiceRequestStatuses.UnderReview, ServiceRequestStatusKeys.UnderReview, StatusKind.Open),
        Status(ServiceRequestStatuses.Returned, ServiceRequestStatusKeys.Returned, StatusKind.Open),
        Status(ServiceRequestStatuses.Resubmitted, ServiceRequestStatusKeys.Resubmitted, StatusKind.Open),
        Status(ServiceRequestStatuses.Approved, ServiceRequestStatusKeys.Approved, StatusKind.Terminal),
        Status(ServiceRequestStatuses.Rejected, ServiceRequestStatusKeys.Rejected, StatusKind.Terminal),
        Status(ServiceRequestStatuses.Cancelled, ServiceRequestStatusKeys.Cancelled, StatusKind.Terminal),
    ];

    /// <summary>The transitions: one rule per action and from-status.</summary>
    public static IReadOnlyList<TransitionRule> Rules { get; } =
    [
        Requester(ServiceRequestActions.Submit, new(ServiceRequestStatuses.Draft, ServiceRequestStatuses.Submitted), ReasonRequirement.None, ToRequester(ServiceRequestNotificationKeys.Submitted)),
        Staff(ServiceRequestActions.StartReview, new(ServiceRequestStatuses.Submitted, ServiceRequestStatuses.UnderReview), ServiceRequestsPermissionKeys.Review, ReasonRequirement.None, ToRequester(ServiceRequestNotificationKeys.UnderReview)),
        Staff(ServiceRequestActions.StartReview, new(ServiceRequestStatuses.Resubmitted, ServiceRequestStatuses.UnderReview), ServiceRequestsPermissionKeys.Review, ReasonRequirement.None, ToRequester(ServiceRequestNotificationKeys.UnderReview)),
        Staff(ServiceRequestActions.Return, new(ServiceRequestStatuses.UnderReview, ServiceRequestStatuses.Returned), ServiceRequestsPermissionKeys.Decide, ReasonRequirement.Required, ToRequester(ServiceRequestNotificationKeys.Returned)),
        Requester(ServiceRequestActions.Resubmit, new(ServiceRequestStatuses.Returned, ServiceRequestStatuses.Resubmitted), ReasonRequirement.None, ToAssignee(ServiceRequestNotificationKeys.Resubmitted)),
        Staff(ServiceRequestActions.Approve, new(ServiceRequestStatuses.UnderReview, ServiceRequestStatuses.Approved), ServiceRequestsPermissionKeys.Decide, ReasonRequirement.None, ToRequester(ServiceRequestNotificationKeys.Approved)),
        Staff(ServiceRequestActions.Reject, new(ServiceRequestStatuses.UnderReview, ServiceRequestStatuses.Rejected), ServiceRequestsPermissionKeys.Decide, ReasonRequirement.Required, ToRequester(ServiceRequestNotificationKeys.Rejected)),
        Requester(ServiceRequestActions.Cancel, new(ServiceRequestStatuses.Draft, ServiceRequestStatuses.Cancelled), ReasonRequirement.Optional, NotificationDecision.None),
        Requester(ServiceRequestActions.Cancel, new(ServiceRequestStatuses.Submitted, ServiceRequestStatuses.Cancelled), ReasonRequirement.Optional, ToBoth(ServiceRequestNotificationKeys.Cancelled)),
    ];

    /// <summary>The catalog the kernel validates and runs.</summary>
    public static TransitionCatalog Catalog { get; } = new(ServiceRequestRecord.RecordType, Statuses, Rules);

    /// <summary>The wording key of a status for an audience: <c>serviceRequests.status.&lt;statusKey&gt;.&lt;requester|staff|admin&gt;</c>.</summary>
    /// <exception cref="ArgumentOutOfRangeException">The audience is not defined.</exception>
    public static string WordingKey(string statusKey, WorkflowAudience audience) =>
        string.Join(KeySeparator, KeyPrefix, StatusSegment, statusKey, AudienceSegment(audience));

    /// <summary>The label key of an action: <c>serviceRequests.action.&lt;actionKey&gt;</c>.</summary>
    public static string LabelKey(string actionKey) => string.Join(KeySeparator, KeyPrefix, ActionSegment, actionKey);

    private static string AudienceSegment(WorkflowAudience audience) => audience switch
    {
        WorkflowAudience.Requester => AudienceSegments.Requester,
        WorkflowAudience.Staff => AudienceSegments.Staff,
        WorkflowAudience.Admin => AudienceSegments.Admin,
        _ => throw new ArgumentOutOfRangeException(nameof(audience), audience, "Not a workflow audience."),
    };

    private static WorkflowStatusDefinition Status(int status, string key, StatusKind kind) => new(
        status,
        key,
        IsInitial: kind == StatusKind.Initial,
        IsTerminal: kind == StatusKind.Terminal,
        new StatusWording(
            RequesterKey: WordingKey(key, WorkflowAudience.Requester),
            StaffKey: WordingKey(key, WorkflowAudience.Staff),
            AdminKey: WordingKey(key, WorkflowAudience.Admin)));

    private static TransitionRule Requester(string action, Move move, ReasonRequirement reason, NotificationDecision notification) => new(
        action,
        FromStatus: move.From,
        ToStatus: move.To,
        ServiceRequestsPermissionKeys.Create,
        TransitionSide.Requester,
        reason,
        ReasonFieldKey: ReasonFieldOf(reason),
        LabelKey: LabelKey(action),
        notification,
        RequiresOwnership: true);

    private static TransitionRule Staff(string action, Move move, string permissionKey, ReasonRequirement reason, NotificationDecision notification) => new(
        action,
        FromStatus: move.From,
        ToStatus: move.To,
        permissionKey,
        TransitionSide.Staff,
        reason,
        ReasonFieldKey: ReasonFieldOf(reason),
        LabelKey: LabelKey(action),
        notification,
        RequiresOwnership: false);

    private static string? ReasonFieldOf(ReasonRequirement reason) => reason == ReasonRequirement.None ? null : ReasonField;

    private static NotificationDecision ToRequester(string template) => new(template, NotifyRequester: true, NotifyAssignee: false);

    private static NotificationDecision ToAssignee(string template) => new(template, NotifyRequester: false, NotifyAssignee: true);

    private static NotificationDecision ToBoth(string template) => new(template, NotifyRequester: true, NotifyAssignee: true);

    private readonly record struct Move(int From, int To);

    private enum StatusKind
    {
        Initial = 1,
        Open = 2,
        Terminal = 3,
    }

    private static class AudienceSegments
    {
        public const string Requester = "requester";
        public const string Staff = "staff";
        public const string Admin = "admin";
    }
}

/// <summary>The sample's workflow, registered with <c>AddRecordWorkflow&lt;ServiceRequestWorkflowDefinition&gt;()</c>.</summary>
public sealed class ServiceRequestWorkflowDefinition : IRecordWorkflowDefinition
{
    /// <inheritdoc />
    public string RecordType => ServiceRequestRecord.RecordType;

    /// <inheritdoc />
    public TransitionCatalog Catalog => ServiceRequestWorkflow.Catalog;
}
