using Starter.DocsGen.Collection;

namespace Starter.DocsGen.Rendering;

/// <summary>
/// Renders <c>workflows/&lt;record-type&gt;.md</c>, the sign-off sheet of one workflow: the Mermaid state diagram, the
/// statuses with their audience wording keys, and every transition with its permission, side, ownership, reason and
/// notification decision.
/// </summary>
internal static class WorkflowRenderer
{
    private const string TitlePrefix = "Workflow: ";
    private const string DiagramHeading = "Diagram";
    private const string StatusesHeading = "Statuses";
    private const string TransitionsHeading = "Transitions";
    private const string NoTransitions = "No transitions.";
    private const int SectionHeadingLevel = 2;

    private const string DiagramNote =
        "Each arrow is one transition rule: the action, the permission the actor must hold and whether a reason is " +
        "asked for. `[*]` marks where a record starts and where it ends; terminal statuses are bold and offer no action.";

    private const string TransitionsNote =
        "Allowed actions are computed on the server from this table (side, permission, ownership and status); screens " +
        "never hard-code them. A required reason is refused when blank.";

    private static readonly string[] TransitionHeaders =
        ["Action", "From", "To", "Side", "Permission", "Owner only", "Reason", "Reason field", "Label key", "Notification"];

    /// <summary>The document of <paramref name="workflow"/>.</summary>
    public static string Render(WorkflowDoc workflow)
    {
        ArgumentNullException.ThrowIfNull(workflow);
        var catalog = workflow.Catalog;
        var document = MarkdownText.Document(TitlePrefix + workflow.RecordType)
            .Line($"The workflow of the {MarkdownText.Code(workflow.RecordType)} record type, declared by {workflow.Owner} in {MarkdownText.Code(workflow.DefinitionType)}.")
            .Line()
            .Heading(SectionHeadingLevel, DiagramHeading)
            .Line(DiagramNote)
            .Line()
            .Line(MermaidStateDiagram.Render(catalog).TrimEnd(MarkdownText.NewLine))
            .Line()
            .Heading(SectionHeadingLevel, StatusesHeading);
        WorkflowText.StatusTable(document, catalog)
            .Heading(SectionHeadingLevel, TransitionsHeading)
            .Line(TransitionsNote)
            .Line();
        var rows = WorkflowText.OrderedRules(catalog).Select(rule => (IReadOnlyList<string>)
        [
            MarkdownText.Code(rule.ActionKey),
            WorkflowText.StatusName(catalog, rule.FromStatus),
            WorkflowText.StatusName(catalog, rule.ToStatus),
            WorkflowText.Describe(rule.Side),
            MarkdownText.Code(rule.PermissionKey),
            WorkflowText.Flag(rule.RequiresOwnership),
            WorkflowText.Describe(rule.Reason),
            MarkdownText.Code(rule.ReasonFieldKey),
            MarkdownText.Code(rule.LabelKey),
            WorkflowText.Describe(rule.Notification),
        ]);
        return document.Table(TransitionHeaders, rows, NoTransitions).ToString();
    }
}
