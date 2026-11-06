using Starter.Application.Records;
using Starter.Modules.ServiceRequests.Domain;

namespace Starter.Modules.ServiceRequests.Application.Workflow;

/// <summary>
/// The sample record type as the Records kernel registers it: key <c>service-request</c>, prefix <c>SR</c>
/// (<c>SR-2026-001</c>), the queue and view-own permissions, and the attachable record type of its files.
/// </summary>
public sealed class ServiceRequestRecordTypeSource : IRecordTypeRegistrationSource
{
    /// <summary>The one registration of the sample.</summary>
    public static RecordTypeRegistration Registration { get; } = new(
        RecordType: ServiceRequestRecord.RecordType,
        NumberPrefix: ServiceRequestRecord.NumberPrefix,
        QueuePermissionKey: ServiceRequestsPermissionKeys.Queue,
        ViewOwnPermissionKey: ServiceRequestsPermissionKeys.ViewOwn,
        AttachableRecordType: ServiceRequestRecord.AttachableRecordType);

    /// <inheritdoc />
    public IEnumerable<RecordTypeRegistration> Registrations { get; } = [Registration];
}

/// <summary>
/// The sample's SLA: <see cref="ServiceRequestsLimits.ReviewSla"/> (3 days) on Under Review, "due soon"
/// <see cref="ServiceRequestsLimits.ReviewDueSoonBefore"/> (1 day) before, and the escalation template
/// <see cref="ServiceRequestNotificationKeys.SlaEscalation"/> when breached (raised by the Deadlines SLA scan).
/// </summary>
public sealed class ServiceRequestSlaPolicySource : ISlaPolicySource
{
    /// <summary>The Under Review SLA.</summary>
    public static SlaDefinition UnderReview { get; } = new(
        RecordType: ServiceRequestRecord.RecordType,
        Status: ServiceRequestStatuses.UnderReview,
        Duration: ServiceRequestsLimits.ReviewSla,
        DueSoonBefore: ServiceRequestsLimits.ReviewDueSoonBefore,
        EscalationTemplateKey: ServiceRequestNotificationKeys.SlaEscalation);

    /// <inheritdoc />
    public IEnumerable<SlaDefinition> Definitions { get; } = [UnderReview];
}
