using Starter.Application.Notifications;
using Starter.Domain.Localization;
using Starter.Modules.Notifications.Domain;

namespace Starter.Modules.Notifications.Infrastructure.Persistence.Migrations;

/// <summary>
/// The notification texts of the sample record type <c>service-request</c> (module ServiceRequests) that
/// <see cref="InitialCreate"/> inserts after <see cref="TemplateSeed"/> (folded in by the v1.0 squash, ADR-010; once
/// the migration <c>AddServiceRequestTemplates</c>): one template per workflow transition that notifies someone, plus
/// the SLA escalation, each in Arabic and English on the in-app and e-mail channels. Frozen with the migration: changed
/// wording is a NEW migration. The keys are frozen here as data (this module never references the sample); the
/// sample's tests prove they equal its <c>ServiceRequestNotificationKeys</c>. Removing the sample module leaves these
/// rows; they are never sent again. Placeholders (<c>{{name}}</c>):
/// <see cref="RecordNumber"/> in every text, plus <see cref="DueAt"/> in the SLA escalation. In-app bodies are plain
/// text; e-mail bodies are HTML (the Arabic ones right to left).
/// </summary>
public static class ServiceRequestTemplateSeed
{
    /// <summary>The reference number placeholder (for example <c>SR-2026-001</c>).</summary>
    public const string RecordNumber = "recordNumber";

    /// <summary>The SLA due time placeholder (display text); the escalation only.</summary>
    public const string DueAt = "dueAt";

    /// <summary>The rows, by key, channel and language.</summary>
    public static IReadOnlyList<TemplateText> Rows { get; } =
    [
        .. Both(
            ServiceRequestTemplateKeys.Submitted,
            arabic: new("تم تقديم طلبك {{recordNumber}}", "استلمنا طلبك رقم {{recordNumber}}، وسيبدأ فريقنا مراجعته قريباً."),
            english: new("Your request {{recordNumber}} was submitted", "We received your request {{recordNumber}}. Our team will start reviewing it soon.")),
        .. Both(
            ServiceRequestTemplateKeys.UnderReview,
            arabic: new("طلبك {{recordNumber}} قيد المراجعة", "بدأ فريقنا مراجعة طلبك رقم {{recordNumber}}."),
            english: new("Your request {{recordNumber}} is under review", "Our team has started reviewing your request {{recordNumber}}.")),
        .. Both(
            ServiceRequestTemplateKeys.Returned,
            arabic: new("أُعيد طلبك {{recordNumber}} لاستكمال البيانات", "أُعيد إليك طلبك رقم {{recordNumber}}. اطّلع على السبب وعدّل الطلب ثم أعد تقديمه."),
            english: new("Your request {{recordNumber}} was returned", "Your request {{recordNumber}} was returned to you. Read the reason, update the request and resubmit it.")),
        .. Both(
            ServiceRequestTemplateKeys.Resubmitted,
            arabic: new("أُعيد تقديم الطلب {{recordNumber}}", "عدّل مقدّم الطلب رقم {{recordNumber}} بياناته وأعاد تقديمه، وهو جاهز للمراجعة."),
            english: new("Request {{recordNumber}} was resubmitted", "The requester updated request {{recordNumber}} and resubmitted it. It is ready for review.")),
        .. Both(
            ServiceRequestTemplateKeys.Approved,
            arabic: new("تمت الموافقة على طلبك {{recordNumber}}", "تمت الموافقة على طلبك رقم {{recordNumber}}."),
            english: new("Your request {{recordNumber}} was approved", "Your request {{recordNumber}} was approved.")),
        .. Both(
            ServiceRequestTemplateKeys.Rejected,
            arabic: new("رُفض طلبك {{recordNumber}}", "رُفض طلبك رقم {{recordNumber}}. اطّلع على السبب في تفاصيل الطلب."),
            english: new("Your request {{recordNumber}} was rejected", "Your request {{recordNumber}} was rejected. The reason is shown in the request details.")),
        .. Both(
            ServiceRequestTemplateKeys.Cancelled,
            arabic: new("أُلغي الطلب {{recordNumber}}", "أُلغي الطلب رقم {{recordNumber}} بناءً على طلب مقدّمه."),
            english: new("Request {{recordNumber}} was cancelled", "Request {{recordNumber}} was cancelled by its requester.")),
        .. Both(
            ServiceRequestTemplateKeys.SlaEscalation,
            arabic: new("تجاوز الطلب {{recordNumber}} مدة المراجعة", "تجاوز الطلب رقم {{recordNumber}} المدة المحددة لمراجعته، وكان موعدها {{dueAt}}. يرجى إنهاء المراجعة."),
            english: new("Request {{recordNumber}} is overdue", "Request {{recordNumber}} passed its review deadline of {{dueAt}}. Please complete the review.")),
    ];

    private static TemplateText[] Both(string key, Wording arabic, Wording english) =>
    [
        new(key, NotificationChannels.InApp, AppLanguage.Arabic, arabic.Subject, arabic.Body),
        new(key, NotificationChannels.InApp, AppLanguage.English, english.Subject, english.Body),
        new(key, NotificationChannels.Email, AppLanguage.Arabic, arabic.Subject, "<div dir=\"rtl\"><p>" + arabic.Body + "</p></div>"),
        new(key, NotificationChannels.Email, AppLanguage.English, english.Subject, "<p>" + english.Body + "</p>"),
    ];

    private sealed record Wording(string Subject, string Body);
}

/// <summary>The template keys <see cref="ServiceRequestTemplateSeed"/> seeds, frozen as data.</summary>
public static class ServiceRequestTemplateKeys
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

    /// <summary>Cancelled (to the requester and the assignee).</summary>
    public const string Cancelled = "service-requests.cancelled";

    /// <summary>The SLA on Under Review is breached (to the assignee).</summary>
    public const string SlaEscalation = "service-requests.sla-escalation";
}
