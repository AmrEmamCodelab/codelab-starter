using Starter.Tools.DocsGate.Markdown;
using Starter.Tools.DocsGate.Repository;

namespace Starter.Tools.DocsGate.Checks;

/// <summary>DOCS003: every local link or file reference in a Markdown file points at an existing path (exact case).</summary>
internal static class DeadLinkCheck
{
    public static IEnumerable<Finding> Check(string root, IEnumerable<string> markdownFiles)
    {
        foreach (var relativePath in markdownFiles)
        {
            var fullPath = RepositoryPaths.ToFullPath(root: root, relativePath: relativePath);
            var documentDirectory = Path.GetDirectoryName(fullPath) ?? root;
            foreach (var link in MarkdownLinkExtractor.Extract(File.ReadAllText(fullPath)))
            {
                var targetPath = LinkTargets.ResolveLocalPath(root: root, documentDirectory: documentDirectory, target: link.Target);
                if (targetPath is not null && !ExactCasePath.Exists(root: root, fullPath: targetPath))
                {
                    yield return new Finding(
                        RuleIds.DeadLink,
                        relativePath,
                        link.Line,
                        $"dead link '{link.Target}': no file or folder at that path (paths are case-sensitive on Linux CI).");
                }
            }
        }
    }
}
