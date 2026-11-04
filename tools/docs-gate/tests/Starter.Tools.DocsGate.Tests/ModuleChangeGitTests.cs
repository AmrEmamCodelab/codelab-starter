using Starter.Tools.DocsGate.Checks;
using Starter.Tools.DocsGate.Cli;

namespace Starter.Tools.DocsGate.Tests;

/// <summary>End-to-end DOCS005 against real git repositories, including the Docs-Not-Needed override.</summary>
public sealed class ModuleChangeGitTests
{
    private const string Module = "src/Records";
    private const string ModuleFile = "src/Records/Service.cs";

    [Fact]
    public void A_working_tree_change_without_docs_fails()
    {
        using var repository = CommittedModuleRepository();
        repository.Write(ModuleFile, "class Service { int Changed; }\n");

        var run = GateHarness.Check(repository.Root);

        Assert.Equal(ExitCodes.FindingsReported, run.ExitCode);
        Assert.Contains($"{RuleIds.ModuleChangedWithoutDocs} {Module}/MODULE.md", run.Output, StringComparison.Ordinal);
    }

    [Fact]
    public void An_untracked_file_in_a_module_counts_as_a_change()
    {
        using var repository = CommittedModuleRepository();
        repository.Write("src/Records/NewFile.cs", "class NewFile { }\n");

        Assert.Equal(ExitCodes.FindingsReported, GateHarness.Check(repository.Root).ExitCode);
    }

    [Fact]
    public void A_working_tree_change_with_docs_passes()
    {
        using var repository = CommittedModuleRepository();
        repository.Write(ModuleFile, "class Service { int Changed; }\n");
        repository.Write($"{Module}/MODULE.md", TempRepository.FullModuleDoc + "\nChanged.\n");

        Assert.Equal(ExitCodes.Passed, GateHarness.Check(repository.Root).ExitCode);
    }

    [Fact]
    public void The_environment_override_passes_and_says_why()
    {
        using var repository = CommittedModuleRepository();
        repository.Write(ModuleFile, "class Service { int Changed; }\n");
        var environment = new Dictionary<string, string> { [DocsNotNeededOverride.ReasonEnvironmentVariable] = "formatting only" };

        var run = GateHarness.Check(repository.Root, environment: environment);

        Assert.Equal(ExitCodes.Passed, run.ExitCode);
        Assert.Contains("formatting only", run.Output, StringComparison.Ordinal);
    }

    [Fact]
    public void Against_a_base_a_committed_change_without_docs_fails()
    {
        using var repository = CommittedModuleRepository();
        var baseSha = repository.HeadSha();
        repository.Write(ModuleFile, "class Service { int Changed; }\n");
        repository.CommitAll("Change the service");

        var run = GateHarness.Check(repository.Root, baseRef: baseSha);

        Assert.Equal(ExitCodes.FindingsReported, run.ExitCode);
    }

    [Fact]
    public void Against_a_base_the_latest_commit_trailer_overrides()
    {
        using var repository = CommittedModuleRepository();
        var baseSha = repository.HeadSha();
        repository.Write(ModuleFile, "class Service { int Changed; }\n");
        repository.CommitAll("Change the service\n\nDocs-Not-Needed: behaviour unchanged");

        var run = GateHarness.Check(repository.Root, baseRef: baseSha);

        Assert.Equal(ExitCodes.Passed, run.ExitCode);
        Assert.Contains("behaviour unchanged", run.Output, StringComparison.Ordinal);
    }

    [Fact]
    public void A_clean_work_tree_passes()
    {
        using var repository = CommittedModuleRepository();

        Assert.Equal(ExitCodes.Passed, GateHarness.Check(repository.Root).ExitCode);
    }

    [Fact]
    public void A_new_module_on_a_branch_without_commits_passes_when_it_has_docs()
    {
        using var repository = TempRepository.CreateGitRepository().WriteModule(Module).Write(ModuleFile, "class Service { }\n");

        Assert.Equal(ExitCodes.Passed, GateHarness.Check(repository.Root).ExitCode);
    }

    [Fact]
    public void An_unknown_base_is_a_tool_error()
    {
        using var repository = CommittedModuleRepository();

        var run = GateHarness.Check(repository.Root, baseRef: "no-such-branch");

        Assert.Equal(ExitCodes.UsageOrToolError, run.ExitCode);
        Assert.Contains("merge base", run.Error, StringComparison.Ordinal);
    }

    [Fact]
    public void Outside_git_the_check_is_skipped_with_a_note_and_base_is_rejected()
    {
        using var repository = TempRepository.CreateFolder().WriteModule(Module);

        var plain = GateHarness.Check(repository.Root);
        var withBase = GateHarness.Check(repository.Root, baseRef: "main");

        Assert.Equal(ExitCodes.Passed, plain.ExitCode);
        Assert.Contains("DOCS005", plain.Output, StringComparison.Ordinal);
        Assert.Equal(ExitCodes.UsageOrToolError, withBase.ExitCode);
    }

    private static TempRepository CommittedModuleRepository()
    {
        var repository = TempRepository.CreateGitRepository()
            .WriteModule(Module)
            .Write(ModuleFile, "class Service { }\n")
            .Write("README.md", "# Fixture\n");
        repository.CommitAll("Initial");
        return repository;
    }
}
