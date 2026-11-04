namespace Starter.Tools.DocsGate.Checks;

/// <summary>
/// Finds git merge-conflict markers. Start, base and end markers are always reported. The separator line (seven '=')
/// is reported only inside an open conflict, because on its own it is a valid Markdown heading underline.
/// tools/ci/conflict-markers.mjs applies the same rules to every text file in the repo.
/// </summary>
internal static class MergeMarkerScanner
{
    private const int MarkerLength = 7;
    private const char LabelSeparator = ' ';
    private static readonly string StartMarker = new('<', MarkerLength);
    private static readonly string BaseMarker = new('|', MarkerLength);
    private static readonly string SeparatorMarker = new('=', MarkerLength);
    private static readonly string EndMarker = new('>', MarkerLength);

    /// <summary>Returns (1-based line, marker) for every conflict marker in the text.</summary>
    public static IEnumerable<(int Line, string Marker)> Scan(string text)
    {
        var lines = text.Split('\n');
        var insideConflict = false;
        for (var index = 0; index < lines.Length; index++)
        {
            var line = lines[index].TrimEnd('\r');
            var lineNumber = index + 1;
            if (IsMarker(line, StartMarker))
            {
                insideConflict = true;
                yield return (lineNumber, StartMarker);
            }
            else if (IsMarker(line, BaseMarker))
            {
                yield return (lineNumber, BaseMarker);
            }
            else if (insideConflict && string.Equals(line, SeparatorMarker, StringComparison.Ordinal))
            {
                yield return (lineNumber, SeparatorMarker);
            }
            else if (IsMarker(line, EndMarker))
            {
                insideConflict = false;
                yield return (lineNumber, EndMarker);
            }
        }
    }

    // A marker is the seven characters alone, or followed by a space and a label (for example the branch name).
    private static bool IsMarker(string line, string marker) =>
        line.StartsWith(marker, StringComparison.Ordinal)
        && (line.Length == marker.Length || line[marker.Length] == LabelSeparator);
}
