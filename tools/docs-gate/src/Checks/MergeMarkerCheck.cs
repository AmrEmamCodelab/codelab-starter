using Starter.Tools.DocsGate.Repository;

namespace Starter.Tools.DocsGate.Checks;

/// <summary>DOCS004: no merge-conflict markers in documentation files (Markdown and module rules).</summary>
internal static class MergeMarkerCheck
{
    public static IEnumerable<Finding> Check(string root, IEnumerable<string> documentationFiles)
    {
        foreach (var relativePath in documentationFiles)
        {
            var text = File.ReadAllText(RepositoryPaths.ToFullPath(root: root, relativePath: relativePath));
            foreach (var (line, marker) in MergeMarkerScanner.Scan(text))
            {
                yield return new Finding(
                    RuleIds.MergeMarker,
                    relativePath,
                    line,
                    $"merge-conflict marker '{marker}': resolve the conflict.");
            }
        }
    }
}
