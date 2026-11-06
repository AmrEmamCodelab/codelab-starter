using Starter.Application.Workflow;
using Starter.Domain.Workflow;

namespace Starter.Workflow.Testing;

/// <summary>The statuses of the generic <c>test-record</c> type (prefix TR), used only by kernel tests.</summary>
public static class TestRecordStatuses
{
    /// <summary>Initial.</summary>
    public const int Draft = 1;

    /// <summary>Filed by the requester.</summary>
    public const int Submitted = 2;

    /// <summary>Taken by staff.</summary>
    public const int InReview = 3;

    /// <summary>Sent back to the requester with a reason.</summary>
    public const int Returned = 4;

    /// <summary>Terminal.</summary>
    public const int Accepted = 5;

    /// <summary>Terminal; reached with a reason.</summary>
    public const int Declined = 6;

    /// <summary>Terminal; withdrawn by the requester.</summary>
    public const int Withdrawn = 7;

    /// <summary>A status no rule reaches (only <see cref="BrokenCatalogs.Unreachable"/> defines it).</summary>
    public const int Orphan = 9;
}

/// <summary>The action keys of the test-record type.</summary>
public static class TestRecordActions
{
    /// <summary>Draft to Submitted (requester, owner).</summary>
    public const string Submit = "submit";

    /// <summary>Submitted to InReview (staff).</summary>
    public const string Take = "take";

    /// <summary>InReview to Returned (staff, reason required).</summary>
    public const string Return = "return";

    /// <summary>Returned to Submitted (requester, owner).</summary>
    public const string Resubmit = "resubmit";

    /// <summary>InReview to Accepted (staff).</summary>
    public const string Accept = "accept";

    /// <summary>InReview to Declined (staff, reason required).</summary>
    public const string Decline = "decline";

    /// <summary>Draft or Submitted to Withdrawn (requester, owner, reason optional).</summary>
    public const string Withdraw = "withdraw";
}

/// <summary>The permission keys of the test-record type.</summary>
public static class TestRecordPermissionKeys
{
    /// <summary>File, resubmit and withdraw own test records.</summary>
    public const string Create = "test-records.create";

    /// <summary>Take a test record into review.</summary>
    public const string Review = "test-records.review";

    /// <summary>Accept, decline or return a test record.</summary>
    public const string Decide = "test-records.decide";

    /// <summary>Every key above.</summary>
    public static IReadOnlySet<string> All { get; } = new HashSet<string>(StringComparer.Ordinal) { Create, Review, Decide };
}

/// <summary>
/// The valid catalog of the generic <c>test-record</c> type: a small review loop with a return-and-resubmit cycle
/// and three terminal statuses. Kernel tests use it; no module ships it.
/// </summary>
public static class TestRecordWorkflow
{
    /// <summary>The record type key.</summary>
    public const string RecordType = "test-record";

    /// <summary>The record number prefix.</summary>
    public const string NumberPrefix = "TR";

    /// <summary>The form field every reason is entered in.</summary>
    public const string ReasonField = "reason";

    private const string SubmittedTemplate = "test-records.submitted";
    private const string ReturnedTemplate = "test-records.returned";
    private const string ClosedTemplate = "test-records.closed";

    /// <summary>Every status.</summary>
    public static IReadOnlyList<WorkflowStatusDefinition> Statuses { get; } =
    [
        Status(TestRecordStatuses.Draft, "draft", isInitial: true, isTerminal: false),
        Status(TestRecordStatuses.Submitted, "submitted", isInitial: false, isTerminal: false),
        Status(TestRecordStatuses.InReview, "in-review", isInitial: false, isTerminal: false),
        Status(TestRecordStatuses.Returned, "returned", isInitial: false, isTerminal: false),
        Status(TestRecordStatuses.Accepted, "accepted", isInitial: false, isTerminal: true),
        Status(TestRecordStatuses.Declined, "declined", isInitial: false, isTerminal: true),
        Status(TestRecordStatuses.Withdrawn, "withdrawn", isInitial: false, isTerminal: true),
    ];

    /// <summary>Every rule.</summary>
    public static IReadOnlyList<TransitionRule> Rules { get; } =
    [
        Requester(TestRecordActions.Submit, TestRecordStatuses.Draft, TestRecordStatuses.Submitted, ReasonRequirement.None, ToStaff(SubmittedTemplate)),
        Staff(TestRecordActions.Take, TestRecordStatuses.Submitted, TestRecordStatuses.InReview, TestRecordPermissionKeys.Review, ReasonRequirement.None),
        Staff(TestRecordActions.Return, TestRecordStatuses.InReview, TestRecordStatuses.Returned, TestRecordPermissionKeys.Decide, ReasonRequirement.Required) with { Notification = ToRequester(ReturnedTemplate) },
        Requester(TestRecordActions.Resubmit, TestRecordStatuses.Returned, TestRecordStatuses.Submitted, ReasonRequirement.None, ToStaff(SubmittedTemplate)),
        Staff(TestRecordActions.Accept, TestRecordStatuses.InReview, TestRecordStatuses.Accepted, TestRecordPermissionKeys.Decide, ReasonRequirement.None) with { Notification = ToRequester(ClosedTemplate) },
        Staff(TestRecordActions.Decline, TestRecordStatuses.InReview, TestRecordStatuses.Declined, TestRecordPermissionKeys.Decide, ReasonRequirement.Required) with { Notification = ToRequester(ClosedTemplate) },
        Requester(TestRecordActions.Withdraw, TestRecordStatuses.Draft, TestRecordStatuses.Withdrawn, ReasonRequirement.Optional, NotificationDecision.None),
        Requester(TestRecordActions.Withdraw, TestRecordStatuses.Submitted, TestRecordStatuses.Withdrawn, ReasonRequirement.Optional, ToStaff(ClosedTemplate)),
    ];

    /// <summary>The valid catalog.</summary>
    public static TransitionCatalog Catalog { get; } = new(RecordType, Statuses, Rules);

    /// <summary>The i18n key of a status for an audience: <c>testRecords.status.&lt;key&gt;.&lt;audience&gt;</c>.</summary>
    public static string WordingKey(string statusKey, WorkflowAudience audience) =>
        $"testRecords.status.{statusKey}.{audience.ToString().ToLowerInvariant()}";

    /// <summary>Builds a status definition worded for every audience.</summary>
    public static WorkflowStatusDefinition Status(int status, string key, bool isInitial, bool isTerminal) => new(
        status,
        key,
        isInitial,
        isTerminal,
        new StatusWording(
            RequesterKey: WordingKey(key, WorkflowAudience.Requester),
            StaffKey: WordingKey(key, WorkflowAudience.Staff),
            AdminKey: WordingKey(key, WorkflowAudience.Admin)));

    private static TransitionRule Requester(string action, int from, int to, ReasonRequirement reason, NotificationDecision notification) => new(
        action,
        FromStatus: from,
        ToStatus: to,
        TestRecordPermissionKeys.Create,
        TransitionSide.Requester,
        reason,
        ReasonFieldKey: reason == ReasonRequirement.None ? null : ReasonField,
        LabelKey: $"testRecords.action.{action}",
        notification,
        RequiresOwnership: true);

    private static TransitionRule Staff(string action, int from, int to, string permissionKey, ReasonRequirement reason) => new(
        action,
        FromStatus: from,
        ToStatus: to,
        permissionKey,
        TransitionSide.Staff,
        reason,
        ReasonFieldKey: reason == ReasonRequirement.None ? null : ReasonField,
        LabelKey: $"testRecords.action.{action}",
        NotificationDecision.None,
        RequiresOwnership: false);

    private static NotificationDecision ToRequester(string template) => new(template, NotifyRequester: true, NotifyAssignee: false);

    private static NotificationDecision ToStaff(string template) => new(template, NotifyRequester: false, NotifyAssignee: true);
}

/// <summary>The valid test-record workflow as a registrable definition (for hosts built in tests).</summary>
public sealed class TestRecordWorkflowDefinition : IRecordWorkflowDefinition
{
    /// <inheritdoc />
    public string RecordType => TestRecordWorkflow.RecordType;

    /// <inheritdoc />
    public TransitionCatalog Catalog => TestRecordWorkflow.Catalog;
}
