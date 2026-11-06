using System.Globalization;
using System.Text;
using Starter.Domain.Workflow;

namespace Starter.DocsGen.Rendering;

/// <summary>
/// Renders a <see cref="TransitionCatalog"/> as a Mermaid <c>stateDiagram-v2</c> block: every status (labelled
/// <c>key (value)</c>), the start arrow into the initial status, one arrow per rule labelled with its action, its
/// permission and its reason requirement, an end arrow out of every terminal status, and terminal statuses styled with
/// the <see cref="TerminalClass"/> class. Pure and deterministic (statuses by value, rules by from-status then catalog
/// order, LF only).
/// </summary>
internal static class MermaidStateDiagram
{
    /// <summary>The Mermaid class terminal statuses carry.</summary>
    public const string TerminalClass = "terminal";

    /// <summary>The diagram type line.</summary>
    public const string DiagramType = "stateDiagram-v2";

    private const string Fence = "```";
    private const string FenceLanguage = "mermaid";
    private const string Indent = "    ";
    private const string StartEnd = "[*]";
    private const string StateIdPrefix = "s";
    private const string NegativeMarker = "n";
    private const string TerminalStyle = "font-weight:bold,stroke-width:3px";
    private const string ListSeparator = ",";

    /// <summary>The fenced Mermaid block, ending with a newline.</summary>
    public static string Render(TransitionCatalog catalog)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        var text = new StringBuilder();
        Append(text, Fence + FenceLanguage, indented: false);
        Append(text, DiagramType, indented: false);
        var statuses = catalog.Statuses.OrderBy(status => status.Status).ThenBy(status => status.Key, StringComparer.Ordinal).ToList();
        foreach (var status in statuses)
        {
            Append(text, $"state \"{Label(status.Key)} ({Value(status.Status)})\" as {StateId(status.Status)}");
        }

        foreach (var initial in statuses.Where(status => status.IsInitial))
        {
            Append(text, $"{StartEnd} --> {StateId(initial.Status)}");
        }

        foreach (var rule in WorkflowText.OrderedRules(catalog))
        {
            Append(text, $"{StateId(rule.FromStatus)} --> {StateId(rule.ToStatus)} : {TransitionLabel(rule)}");
        }

        var terminals = statuses.Where(status => status.IsTerminal).Select(status => StateId(status.Status)).ToList();
        foreach (var terminal in terminals)
        {
            Append(text, $"{terminal} --> {StartEnd}");
        }

        if (terminals.Count > 0)
        {
            Append(text, $"classDef {TerminalClass} {TerminalStyle}");
            Append(text, $"class {string.Join(ListSeparator, terminals)} {TerminalClass}");
        }

        Append(text, Fence, indented: false);
        return text.ToString();
    }

    /// <summary>The Mermaid id of a status: <c>s3</c> (<c>sn3</c> for -3), never its key, so any key is safe.</summary>
    public static string StateId(int status) =>
        StateIdPrefix + Value(status).Replace("-", NegativeMarker, StringComparison.Ordinal);

    /// <summary>The arrow label of a rule: <c>action (permission, reason requirement)</c>.</summary>
    public static string TransitionLabel(TransitionRule rule)
    {
        ArgumentNullException.ThrowIfNull(rule);
        return Label($"{rule.ActionKey} ({rule.PermissionKey}, reason {WorkflowText.Describe(rule.Reason)})");
    }

    /// <summary>Text safe inside a Mermaid label: no quotes, no statement or comment separators, no line breaks.</summary>
    public static string Label(string? value) =>
        (value ?? string.Empty)
            .Replace('"', '\'')
            .Replace(';', ',')
            .Replace(':', ' ')
            .Replace('#', ' ')
            .Replace('\r', ' ')
            .Replace('\n', ' ')
            .Trim();

    private static string Value(int status) => status.ToString(CultureInfo.InvariantCulture);

    private static void Append(StringBuilder text, string line, bool indented = true) =>
        text.Append(indented ? Indent : string.Empty).Append(line).Append(MarkdownText.NewLine);
}
