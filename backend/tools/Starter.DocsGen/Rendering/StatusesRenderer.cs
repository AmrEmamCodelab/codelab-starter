using System.Globalization;
using Starter.DocsGen.Collection;

namespace Starter.DocsGen.Rendering;

/// <summary>
/// Renders <c>statuses.md</c>: every status enum with its integer values, grouped by owner, then every workflow's
/// statuses (record types declare them as int constants, not enums) with their requester, staff and admin wording
/// keys and a link to the workflow's sheet.
/// </summary>
internal static class StatusesRenderer
{
    /// <summary>The heading of the workflow section.</summary>
    public const string WorkflowsHeading = "Workflow statuses";

    /// <summary>The note when no record type declares a workflow.</summary>
    public const string NoWorkflows = "No record type declares a workflow.";

    private const string Title = "Statuses";
    private const string Intro =
        "Every public `int` enum whose name ends with `Status` in the kernel and the modules, then every workflow's " +
        "statuses. The value is what the database stores and the API sends; the client shows a label from its ar and " +
        "en bundles, never the member name. Add a member with the next value; never renumber one.";

    private const string WorkflowsIntro =
        "Each record type's statuses as its workflow catalog declares them; the screens of each audience show the " +
        "wording key of their column. Transitions and the diagram are in the linked sheet.";

    private const string NoStatuses = "No status enums are declared.";
    private const string NoMembers = "No members.";
    private const int OwnerHeadingLevel = 2;
    private const int EnumHeadingLevel = 3;

    private static readonly string[] Headers = ["Value", "Name", "Description"];

    /// <summary>One section per owner (ordinal order), one table per enum sorted by value, then one table per workflow.</summary>
    public static string Render(IReadOnlyList<StatusDoc> statuses, IReadOnlyList<WorkflowDoc> workflows)
    {
        ArgumentNullException.ThrowIfNull(statuses);
        ArgumentNullException.ThrowIfNull(workflows);
        var document = MarkdownText.Document(Title).Line(Intro).Line();
        if (statuses.Count == 0)
        {
            document.Line(NoStatuses).Line();
        }

        foreach (var owner in statuses.GroupBy(status => status.Owner, StringComparer.Ordinal).OrderBy(group => group.Key, StringComparer.Ordinal))
        {
            document.Heading(OwnerHeadingLevel, owner.Key);
            foreach (var status in owner.OrderBy(status => status.Name, StringComparer.Ordinal).ThenBy(status => status.FullName, StringComparer.Ordinal))
            {
                RenderEnum(document, status);
            }
        }

        RenderWorkflows(document, workflows);
        return document.ToString();
    }

    private static void RenderEnum(MarkdownText document, StatusDoc status)
    {
        document.Heading(EnumHeadingLevel, MarkdownText.Code(status.Name)).Line(MarkdownText.Code(status.FullName));
        if (!string.IsNullOrEmpty(status.Description))
        {
            document.Line().Line(status.Description);
        }

        document.Line();
        var rows = status.Members
            .OrderBy(member => member.Value)
            .ThenBy(member => member.Name, StringComparer.Ordinal)
            .Select(member => (IReadOnlyList<string>)
                [member.Value.ToString(CultureInfo.InvariantCulture), MarkdownText.Code(member.Name), member.Description]);
        document.Table(Headers, rows, NoMembers);
    }

    private static void RenderWorkflows(MarkdownText document, IReadOnlyList<WorkflowDoc> workflows)
    {
        document.Heading(OwnerHeadingLevel, WorkflowsHeading).Line(WorkflowsIntro).Line();
        if (workflows.Count == 0)
        {
            document.Line(NoWorkflows);
            return;
        }

        foreach (var workflow in workflows.OrderBy(workflow => workflow.RecordType, StringComparer.Ordinal))
        {
            var sheet = GeneratedFileNames.Workflow(workflow.RecordType);
            document.Heading(EnumHeadingLevel, MarkdownText.Code(workflow.RecordType))
                .Line($"{workflow.Owner}, {MarkdownText.Code(workflow.DefinitionType)}. Transitions and diagram: [{sheet}]({sheet}).")
                .Line();
            WorkflowText.StatusTable(document, workflow.Catalog);
        }
    }
}
