using Starter.DocsGen.Collection;

namespace Starter.DocsGen.Rendering;

/// <summary>Renders <c>error-codes.md</c>: every error code constant, grouped by owner.</summary>
internal static class ErrorCodesRenderer
{
    private const string Title = "Error codes";
    private const string Intro =
        "Every constant of every public static `*ErrorCodes` class (each module's `<Name>ErrorCodes`, the kernel's " +
        "`PlatformErrorCodes` and the Codelab packages). Refusals carry the code in the problem+json `code` member; " +
        "the client renders `errors.<CODE>` from its ar and en bundles.";

    private const string NoCodes = "No error codes are declared.";
    private const int OwnerHeadingLevel = 2;

    private static readonly string[] Headers = ["Code", "Constant", "Description"];

    /// <summary>One section per owner (ordinal order), each a table sorted by code.</summary>
    public static string Render(IReadOnlyList<ErrorCodeDoc> codes)
    {
        ArgumentNullException.ThrowIfNull(codes);
        var document = MarkdownText.Document(Title).Line(Intro).Line();
        if (codes.Count == 0)
        {
            return document.Line(NoCodes).ToString();
        }

        foreach (var owner in codes.GroupBy(code => code.Owner, StringComparer.Ordinal).OrderBy(group => group.Key, StringComparer.Ordinal))
        {
            var rows = owner
                .OrderBy(code => code.Code, StringComparer.Ordinal)
                .ThenBy(code => code.Constant, StringComparer.Ordinal)
                .Select(code => (IReadOnlyList<string>)[MarkdownText.Code(code.Code), MarkdownText.Code(code.Constant), code.Description]);
            document.Heading(OwnerHeadingLevel, owner.Key).Table(Headers, rows, NoCodes);
        }

        return document.ToString();
    }
}
