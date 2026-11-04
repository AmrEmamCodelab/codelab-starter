using Starter.Tools.DocsGate.Markdown;
using Starter.Tools.DocsGate.Repository;

namespace Starter.Tools.DocsGate.Checks;

/// <summary>DOCS001/DOCS002: every module folder has MODULE.md with all required section headings.</summary>
internal static class ModuleDocsCheck
{
    public static IEnumerable<Finding> Check(string root, IEnumerable<string> moduleDirectories)
    {
        foreach (var moduleDirectory in moduleDirectories)
        {
            var docPath = RepositoryLayout.ModuleDocPath(moduleDirectory);
            var fullPath = RepositoryPaths.ToFullPath(root: root, relativePath: docPath);
            if (!ExactCasePath.Exists(root: root, fullPath: fullPath))
            {
                yield return new Finding(
                    RuleIds.ModuleDocMissing,
                    docPath,
                    Finding.NoLine,
                    $"module folder has {RepositoryLayout.ModuleRulesFileName} but no {RepositoryLayout.ModuleDocFileName}.");
                continue;
            }

            var headings = MarkdownHeadings.Read(File.ReadAllText(fullPath));
            foreach (var section in RepositoryLayout.RequiredModuleDocSections)
            {
                if (!headings.Contains(section, StringComparer.OrdinalIgnoreCase))
                {
                    yield return new Finding(
                        RuleIds.ModuleDocSectionMissing,
                        docPath,
                        Finding.NoLine,
                        $"missing section heading '{section}' (required: {string.Join(", ", RepositoryLayout.RequiredModuleDocSections)}).");
                }
            }
        }
    }
}
