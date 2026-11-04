using Starter.Tools.DocsGate.Repository;

namespace Starter.Tools.DocsGate.Tests;

/// <summary>A throwaway folder (optionally a git repo) under %TEMP%, deleted on dispose.</summary>
public sealed class TempRepository : IDisposable
{
    public const string FullModuleDoc =
        "# Sample module\n\n## Purpose\nWhy.\n\n## Use\nHow.\n\n## Extend\nWhere.\n\n## Rules\nWhat.\n\n## Gotchas\nNone.\n";

    public const string ModuleRules = "module: sample\n";

    private static readonly string[] CommitIdentity =
        ["-c", "user.name=Docs Gate Tests", "-c", "user.email=docs-gate@example.invalid", "-c", "commit.gpgsign=false"];

    private readonly GitClient _git = new();

    private TempRepository(string root) => Root = root;

    public string Root { get; }

    public static TempRepository CreateFolder()
    {
        var root = Path.Combine(Path.GetTempPath(), "docs-gate-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        return new TempRepository(root);
    }

    public static TempRepository CreateGitRepository()
    {
        var repository = CreateFolder();
        repository.Git("init", "--quiet", "--initial-branch=main");
        return repository;
    }

    public TempRepository Write(string relativePath, string content)
    {
        var fullPath = RepositoryPaths.ToFullPath(root: Root, relativePath: relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
        File.WriteAllText(fullPath, content);
        return this;
    }

    /// <summary>Adds a module folder with module.rules.yaml and, when given, MODULE.md.</summary>
    public TempRepository WriteModule(string directory, string? moduleDoc = FullModuleDoc)
    {
        Write(RepositoryPaths.Combine(directory: directory, fileName: RepositoryLayout.ModuleRulesFileName), ModuleRules);
        if (moduleDoc is not null)
        {
            Write(RepositoryLayout.ModuleDocPath(directory), moduleDoc);
        }

        return this;
    }

    public void CommitAll(string message)
    {
        Git("add", "--all");
        Git([.. CommitIdentity, "commit", "--quiet", "-m", message]);
    }

    public string HeadSha() => Git("rev-parse", "HEAD").Trim();

    public string Git(params string[] arguments) => _git.RunChecked(Root, arguments).Output;

    public void Dispose()
    {
        if (!Directory.Exists(Root))
        {
            return;
        }

        // git marks object files read-only; clear that or the delete fails on Windows.
        foreach (var file in Directory.EnumerateFiles(Root, "*", SearchOption.AllDirectories))
        {
            File.SetAttributes(file, FileAttributes.Normal);
        }

        Directory.Delete(Root, recursive: true);
    }
}
