using System.Globalization;
using Starter.DocsGen.Collection;

namespace Starter.DocsGen.Rendering;

/// <summary>
/// Renders <c>statuses.md</c>: every status enum with its integer values, grouped by owner. A stub in phase 5 (the
/// kernel and module state enums); the request statuses and their transitions join it with the workflow (phase 7).
/// </summary>
internal static class StatusesRenderer
{
    private const string Title = "Statuses";
    private const string Intro =
        "Every public `int` enum whose name ends with `Status` in the kernel and the modules. The value is what the " +
        "database stores and the API sends; the client shows a label from its ar and en bundles, never the member " +
        "name. Add a member with the next value; never renumber one.";

    private const string NoStatuses = "No status enums are declared.";
    private const string NoMembers = "No members.";
    private const int OwnerHeadingLevel = 2;
    private const int EnumHeadingLevel = 3;

    private static readonly string[] Headers = ["Value", "Name", "Description"];

    /// <summary>One section per owner (ordinal order), one table per enum, rows sorted by value.</summary>
    public static string Render(IReadOnlyList<StatusDoc> statuses)
    {
        ArgumentNullException.ThrowIfNull(statuses);
        var document = MarkdownText.Document(Title).Line(Intro).Line();
        if (statuses.Count == 0)
        {
            return document.Line(NoStatuses).ToString();
        }

        foreach (var owner in statuses.GroupBy(status => status.Owner, StringComparer.Ordinal).OrderBy(group => group.Key, StringComparer.Ordinal))
        {
            document.Heading(OwnerHeadingLevel, owner.Key);
            foreach (var status in owner.OrderBy(status => status.Name, StringComparer.Ordinal).ThenBy(status => status.FullName, StringComparer.Ordinal))
            {
                RenderEnum(document, status);
            }
        }

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
}
