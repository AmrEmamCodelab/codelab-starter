using System.Globalization;
using Starter.DocsGen.Collection;

namespace Starter.DocsGen.Rendering;

/// <summary>
/// Renders <c>workflows/README.md</c>: one row per workflow with a link to its sheet. Always generated, so the folder
/// and its drift check exist with zero workflows too.
/// </summary>
internal static class WorkflowIndexRenderer
{
    /// <summary>The note when no record type declares a workflow.</summary>
    public const string NoWorkflows = "No record type declares a workflow (`IRecordWorkflowDefinition`).";

    private const string Title = "Workflows";
    private const string Intro =
        "One sheet per record type: the state diagram (Mermaid), the statuses with their requester, staff and admin " +
        "wording keys, and every transition with its permission, reason and notification decision. Use them for " +
        "client sign-off. Add a status or a transition with `node tools/scaffold/new-transition.mjs`.";

    private static readonly string[] Headers = ["Record type", "Owner", "Statuses", "Terminal", "Transitions"];

    /// <summary>The index, rows sorted by record type.</summary>
    public static string Render(IReadOnlyList<WorkflowDoc> workflows)
    {
        ArgumentNullException.ThrowIfNull(workflows);
        var rows = workflows
            .OrderBy(workflow => workflow.RecordType, StringComparer.Ordinal)
            .Select(workflow => (IReadOnlyList<string>)
            [
                $"[{workflow.RecordType}]({GeneratedFileNames.WorkflowFileName(workflow.RecordType)})",
                workflow.Owner,
                Count(workflow.Catalog.Statuses.Count),
                Count(workflow.Catalog.Statuses.Count(status => status.IsTerminal)),
                Count(workflow.Catalog.Rules.Count),
            ]);
        return MarkdownText.Document(Title).Line(Intro).Line().Table(Headers, rows, NoWorkflows).ToString();
    }

    private static string Count(int value) => value.ToString(CultureInfo.InvariantCulture);
}
