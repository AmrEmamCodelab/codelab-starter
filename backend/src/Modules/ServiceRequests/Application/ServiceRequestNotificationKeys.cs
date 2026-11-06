namespace Starter.Modules.ServiceRequests.Application;

/// <summary>
/// The notification template keys of the sample (seeded in Arabic and English, in-app and e-mail, by the Notifications
/// migration <c>AddServiceRequestTemplates</c>, which freezes the same strings as data). Placeholders:
/// <see cref="RecordNumber"/> in every template, plus <see cref="DueAt"/> in <see cref="SlaEscalation"/>.
/// </summary>
public static class ServiceRequestNotificationKeys
{
    /// <summary>Submitted (to the requester).</summary>
    public const string Submitted = "service-requests.submitted";

    /// <summary>Review started (to the requester).</summary>
    public const string UnderReview = "service-requests.under-review";

    /// <summary>Returned for changes (to the requester).</summary>
    public const string Returned = "service-requests.returned";

    /// <summary>Resubmitted (to the assignee).</summary>
    public const string Resubmitted = "service-requests.resubmitted";

    /// <summary>Approved (to the requester).</summary>
    public const string Approved = "service-requests.approved";

    /// <summary>Rejected (to the requester).</summary>
    public const string Rejected = "service-requests.rejected";

    /// <summary>Cancelled after submission (to the requester and the assignee).</summary>
    public const string Cancelled = "service-requests.cancelled";

    /// <summary>The Under Review SLA is breached (sent by the Deadlines module's SLA scan).</summary>
    public const string SlaEscalation = "service-requests.sla-escalation";

    /// <summary>The placeholder of the reference number in every template.</summary>
    public const string RecordNumber = "recordNumber";

    /// <summary>The placeholder of the SLA due time in <see cref="SlaEscalation"/>.</summary>
    public const string DueAt = "dueAt";

    /// <summary>Every template key, in a stable order.</summary>
    public static IReadOnlyList<string> All { get; } =
        [Submitted, UnderReview, Returned, Resubmitted, Approved, Rejected, Cancelled, SlaEscalation];
}
