using Starter.Tools.DocsGate.Cli;
using Starter.Tools.DocsGate.Repository;

namespace Starter.Tools.DocsGate.Checks;

/// <summary>Runs every docs-gate rule over one root and collects the findings.</summary>
internal sealed class DocsGateRunner(IGitClient git, Func<string, string?> readEnvironment)
{
    public GateReport Run(CheckOptions options)
    {
        var root = options.Root;
        var isWorkTree = git.IsWorkTree(root);
        var files = RepositoryFiles.List(root: root, git: git, useGit: isWorkTree);
        var modules = RepositoryLayout.FindModuleDirectories(files);

        var findings = new List<Finding>();
        var notices = new List<string>();
        findings.AddRange(ModuleDocsCheck.Check(root, modules));
        findings.AddRange(DeadLinkCheck.Check(root, files.Where(RepositoryLayout.IsMarkdown)));
        findings.AddRange(MergeMarkerCheck.Check(root, files.Where(RepositoryLayout.IsDocumentation)));
        findings.AddRange(CheckModuleChanges(options, isWorkTree, modules, notices));
        return new GateReport(findings, notices);
    }

    private IReadOnlyList<Finding> CheckModuleChanges(
        CheckOptions options,
        bool isWorkTree,
        IReadOnlyCollection<string> modules,
        List<string> notices)
    {
        if (!isWorkTree)
        {
            if (options.BaseRef is not null)
            {
                throw new GitCommandException($"--base needs a git work tree, but '{options.Root}' is not one.");
            }

            notices.Add("not a git work tree: the module-changed check (DOCS005) was skipped.");
            return [];
        }

        var changes = new ChangeDetector(git, options.Root);
        var changedFiles = changes.ChangedFiles(options.BaseRef);
        var reason = DocsNotNeededOverride.Resolve(readEnvironment, changes.LatestCommitMessage());
        return ModuleChangeCheck.Check(
            moduleDirectories: modules,
            changedFiles: changedFiles,
            docsNotNeededReason: reason,
            notices: notices);
    }
}
