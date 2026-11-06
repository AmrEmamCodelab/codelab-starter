using System.Globalization;
using Starter.Domain.Workflow;

namespace Starter.DocsGen.Rendering;

/// <summary>
/// The words the workflow docs use for catalog values, and the one status table both <c>statuses.md</c> and each
/// <c>workflows/&lt;record-type&gt;.md</c> show. Docs only: screens word statuses and actions from i18n keys.
/// </summary>
internal static class WorkflowText
{
    /// <summary>The cell of a true flag.</summary>
    public const string Yes = "yes";

    /// <summary>The cell of a false flag (empty, so the flags stand out).</summary>
    public const string No = "";

    /// <summary>The note of a catalog without statuses.</summary>
    public const string NoStatuses = "No statuses.";

    /// <summary>The notification cell of a rule that has no decision (a catalog violation; the host refuses to start).</summary>
    public const string MissingDecision = "missing (catalog violation)";

    /// <summary>The notification cell of <see cref="NotificationDecision.None"/>.</summary>
    public const string NotifiesNobody = "nobody (on purpose)";

    private const string RequesterRecipient = "requester";
    private const string AssigneeRecipient = "assignee";
    private const string RecipientSeparator = ", ";
    private const string NoTemplate = "no template";
    private const string NoRecipient = "nobody";

    private static readonly string[] StatusHeaders = ["Value", "Key", "Initial", "Terminal", "Requester wording", "Staff wording", "Admin wording"];

    /// <summary>The reason requirement in words: <c>none</c>, <c>optional</c>, <c>required</c>.</summary>
    public static string Describe(ReasonRequirement reason) => reason switch
    {
        ReasonRequirement.None => "none",
        ReasonRequirement.Optional => "optional",
        ReasonRequirement.Required => "required",
        _ => reason.ToString(),
    };

    /// <summary>The side in words: <c>requester</c>, <c>staff</c>, <c>system</c>.</summary>
    public static string Describe(TransitionSide side) => side switch
    {
        TransitionSide.Requester => "requester",
        TransitionSide.Staff => "staff",
        TransitionSide.System => "system",
        _ => side.ToString(),
    };

    /// <summary>A notification decision in words: the template key, then who receives it.</summary>
    public static string Describe(NotificationDecision? decision)
    {
        if (decision is null)
        {
            return MissingDecision;
        }

        if (decision.TemplateKey is null && !decision.NotifiesAnyone)
        {
            return NotifiesNobody;
        }

        var recipients = new List<string>();
        if (decision.NotifyRequester)
        {
            recipients.Add(RequesterRecipient);
        }

        if (decision.NotifyAssignee)
        {
            recipients.Add(AssigneeRecipient);
        }

        var template = decision.TemplateKey is null ? NoTemplate : MarkdownText.Code(decision.TemplateKey);
        var to = recipients.Count == 0 ? NoRecipient : string.Join(RecipientSeparator, recipients);
        return $"{template} to {to}";
    }

    /// <summary>A status as the tables name it: <c>`key` (value)</c>, or the bare value when the catalog does not define it.</summary>
    public static string StatusName(TransitionCatalog catalog, int status)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        var value = status.ToString(CultureInfo.InvariantCulture);
        return catalog.FindStatus(status) is { } definition ? $"{MarkdownText.Code(definition.Key)} ({value})" : value;
    }

    /// <summary>A flag cell.</summary>
    public static string Flag(bool value) => value ? Yes : No;

    /// <summary>Appends the status table of <paramref name="catalog"/>: one row per status, sorted by value.</summary>
    public static MarkdownText StatusTable(MarkdownText document, TransitionCatalog catalog)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(catalog);
        var rows = catalog.Statuses
            .OrderBy(status => status.Status)
            .ThenBy(status => status.Key, StringComparer.Ordinal)
            .Select(status => (IReadOnlyList<string>)
            [
                status.Status.ToString(CultureInfo.InvariantCulture),
                MarkdownText.Code(status.Key),
                Flag(status.IsInitial),
                Flag(status.IsTerminal),
                MarkdownText.Code(status.Wording?.RequesterKey),
                MarkdownText.Code(status.Wording?.StaffKey),
                MarkdownText.Code(status.Wording?.AdminKey),
            ]);
        return document.Table(StatusHeaders, rows, NoStatuses);
    }

    /// <summary>The rules in document order: by from-status, then catalog order (the order screens list the actions).</summary>
    public static IEnumerable<TransitionRule> OrderedRules(TransitionCatalog catalog)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        return catalog.Rules.OrderBy(rule => rule.FromStatus);
    }
}
