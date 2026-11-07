using Codelab.Security.PublicIds;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Starter.Application.Authorization;
using Starter.Application.Records;
using Starter.Application.Workflow;
using Starter.Domain.Workflow;
using Starter.Infrastructure.Authorization;
using Starter.Infrastructure.Results;
using Starter.Modules.Records.Api;
using Starter.Modules.Records.Application;

namespace Starter.Integration.Tests.Records;

/// <summary>
/// The TEST-ONLY record type of the Records integration tests (<c>test-record</c>, prefix <c>TR</c>): what a record-type
/// module declares. Draft (1) -> Submitted (2) -> In review (3) -> Accepted (5) | Declined (6) | Returned (4) ->
/// Submitted; Draft/Submitted -> Cancelled (7). Accepted, Declined and Cancelled are terminal.
/// </summary>
public static class ProbeRecord
{
    public const string RecordType = "test-record";
    public const string NumberPrefix = "TR";
    public const string AttachableRecordType = "test-record-files";
    public const string RequiredDocumentKey = "test-record-document";
    public const string ReasonField = "reason";
    public const string Take = "take";
    public const string Return = "return";
    public const string Accept = "accept";
    public const string Decline = "decline";
    public const string ReturnedTemplate = "test-records.returned";

    public const int Draft = 1;
    public const int Submitted = 2;
    public const int InReview = 3;
    public const int Returned = 4;
    public const int Accepted = 5;
    public const int Declined = 6;
    public const int Cancelled = 7;

    /// <summary>The id of the required attachment type seeded for the probe (far from every real seed).</summary>
    public const int RequiredDocumentTypeId = 9701;

    public const string Create = "test-records.create";
    public const string ViewOwn = "test-records.view-own";
    public const string Queue = "test-records.queue";
    public const string Decide = "test-records.decide";
    // codelab:module-begin Attachments

    /// <summary>The SQL a migration of the owning module would run: one required, single-file PDF type.</summary>
    public const string RequiredDocumentInsert =
        "INSERT INTO [attachments].[ServiceAttachments] ([Id], [Key], [NameAr], [NameEn], [RecordType], [IsRequired], [MaxFiles], [IsActive], [AllowedExtensions]) "
        + "VALUES (@id, @key, @nameAr, @nameEn, @recordType, 1, 1, 1, @extensions)";
    // codelab:module-end Attachments

    public static IReadOnlyCollection<string> RequesterPermissions { get; } =
    [
        Create,
        ViewOwn,
        AttachmentsKeys.View, // codelab:module Attachments
        AttachmentsKeys.Upload, // codelab:module Attachments
    ];
    // codelab:module-begin Attachments

    public static IReadOnlyDictionary<string, object> RequiredDocumentValues { get; } = new Dictionary<string, object>
    {
        ["@id"] = RequiredDocumentTypeId,
        ["@key"] = RequiredDocumentKey,
        ["@nameAr"] = "مستند الاختبار",
        ["@nameEn"] = "Test document",
        ["@recordType"] = AttachableRecordType,
        ["@extensions"] = ".pdf",
    };
    // codelab:module-end Attachments

    public static TransitionCatalog Catalog { get; } = new(
        RecordType,
        [
            Status(Draft, "draft", isInitial: true, isTerminal: false),
            Status(Submitted, "submitted", isInitial: false, isTerminal: false),
            Status(InReview, "in-review", isInitial: false, isTerminal: false),
            Status(Returned, "returned", isInitial: false, isTerminal: false),
            Status(Accepted, "accepted", isInitial: false, isTerminal: true),
            Status(Declined, "declined", isInitial: false, isTerminal: true),
            Status(Cancelled, "cancelled", isInitial: false, isTerminal: true),
        ],
        [
            Requester(RecordLifecycleActions.Submit, Draft, Submitted, ReasonRequirement.None),
            Staff(Take, Submitted, InReview, ReasonRequirement.None, NotificationDecision.None),
            Staff(Return, InReview, Returned, ReasonRequirement.Required, new NotificationDecision(ReturnedTemplate, NotifyRequester: true, NotifyAssignee: false)),
            Requester(RecordLifecycleActions.Resubmit, Returned, Submitted, ReasonRequirement.None),
            Staff(Accept, InReview, Accepted, ReasonRequirement.None, NotificationDecision.None),
            Staff(Decline, InReview, Declined, ReasonRequirement.Required, NotificationDecision.None),
            Requester(RecordLifecycleActions.Cancel, Draft, Cancelled, ReasonRequirement.Optional),
            Requester(RecordLifecycleActions.Cancel, Submitted, Cancelled, ReasonRequirement.Optional),
        ]);

    private static WorkflowStatusDefinition Status(int status, string key, bool isInitial, bool isTerminal) => new(
        status, key, isInitial, isTerminal, new StatusWording($"testRecords.status.{key}.requester", $"testRecords.status.{key}.staff", $"testRecords.status.{key}.admin"));

    private static TransitionRule Requester(string action, int from, int to, ReasonRequirement reason) => new(
        action, FromStatus: from, ToStatus: to, Create, TransitionSide.Requester, reason,
        ReasonFieldKey: reason == ReasonRequirement.None ? null : ReasonField, $"testRecords.action.{action}", NotificationDecision.None, RequiresOwnership: true);

    private static TransitionRule Staff(string action, int from, int to, ReasonRequirement reason, NotificationDecision notification) => new(
        action, FromStatus: from, ToStatus: to, Decide, TransitionSide.Staff, reason,
        ReasonFieldKey: reason == ReasonRequirement.None ? null : ReasonField, $"testRecords.action.{action}", notification, RequiresOwnership: false);
    // codelab:module-begin Attachments

    /// <summary>The attachments permission keys (the Attachments module's constants, as plain keys for the grant call).</summary>
    public static class AttachmentsKeys
    {
        public const string View = Starter.Modules.Attachments.Application.AttachmentsPermissionKeys.View;
        public const string Upload = Starter.Modules.Attachments.Application.AttachmentsPermissionKeys.Upload;
    }
    // codelab:module-end Attachments

    public sealed class WorkflowDefinition : IRecordWorkflowDefinition
    {
        public string RecordType => ProbeRecord.RecordType;

        public TransitionCatalog Catalog => ProbeRecord.Catalog;
    }

    public sealed class RegistrationSource : IRecordTypeRegistrationSource
    {
        public IEnumerable<RecordTypeRegistration> Registrations { get; } =
            [new(RecordType, NumberPrefix, QueuePermissionKey: Queue, ViewOwnPermissionKey: ViewOwn, AttachableRecordType: AttachableRecordType)];
    }

    public sealed class PermissionCatalog : IPermissionCatalogSource
    {
        public IReadOnlyCollection<PermissionDefinition> Permissions { get; } =
        [
            new(Create, "Create and submit test records."),
            new(ViewOwn, "View own test records."),
            new(Queue, "See the test record queue."),
            new(Decide, "Decide on test records."),
        ];
    }
}

/// <summary>
/// The probe's typed endpoints, as a record-type module writes them over <see cref="IRecordLifecycle"/>: create a draft
/// and submit it (the generic API covers everything else).
/// </summary>
[ApiController]
[Route("api/test-records")]
public sealed class ProbeRecordsController(IRecordLifecycle lifecycle) : ControllerBase
{
    [HttpPost]
    [HasPermission(ProbeRecord.Create)]
    public async Task<IActionResult> CreateAsync(CancellationToken cancellationToken)
    {
        var record = await lifecycle.CreateDraftAsync(ProbeRecord.RecordType, cancellationToken);
        return StatusCode(StatusCodes.Status201Created, new { id = PublicId<RecordsIds>.From(record.Id) });
    }

    [HttpPost("{id:publicid}/submit")]
    [HasPermission(ProbeRecord.Create)]
    public async Task<IActionResult> SubmitAsync(PublicId<RecordsIds> id, CancellationToken cancellationToken)
    {
        var outcome = (await lifecycle.SubmitAsync(new(id.Value, ProbeRecord.RecordType), cancellationToken)).ToOutcome();
        return outcome.IsSuccess ? Ok(new { number = outcome.Value }) : outcome.Refusal.ToActionResult();
    }
}
