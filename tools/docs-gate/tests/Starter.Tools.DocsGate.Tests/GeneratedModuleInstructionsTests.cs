using Starter.Tools.DocsGate.Checks;
using Starter.Tools.DocsGate.Cli;
using Starter.Tools.DocsGate.Repository;

namespace Starter.Tools.DocsGate.Tests;

/// <summary>DOCS005 ignores the nested CLAUDE.md that tools/rules-engine regenerates, and nothing else.</summary>
public sealed class GeneratedModuleInstructionsTests
{
    private const string Module = "src/Records";
    private const string ModuleInstructions = "src/Records/CLAUDE.md";
    private const string GeneratedHeader =
        GeneratedModuleInstructions.Marker + " from src/Records/module.rules.yaml (+ rules/base.rules.yaml). Do not edit. -->\n";

    [Fact]
    public void A_regenerated_module_claude_md_alone_is_not_a_module_change()
    {
        using var repository = CommittedRepository(GeneratedHeader + "# Rules\n- automated from P1\n");
        repository.Write(ModuleInstructions, GeneratedHeader + "# Rules\n- automated from P2\n");

        var run = GateHarness.Check(repository.Root);

        Assert.Equal(ExitCodes.Passed, run.ExitCode);
    }

    [Fact]
    public void A_regenerated_module_claude_md_is_ignored_against_a_base_too()
    {
        using var repository = CommittedRepository(GeneratedHeader + "# Rules\n- automated from P1\n");
        var baseSha = repository.HeadSha();
        repository.Write(ModuleInstructions, GeneratedHeader + "# Rules\n- automated from P2\n");
        repository.CommitAll("Recompile rules");

        Assert.Equal(ExitCodes.Passed, GateHarness.Check(repository.Root, baseRef: baseSha).ExitCode);
    }

    [Fact]
    public void A_hand_written_module_claude_md_change_still_needs_docs()
    {
        using var repository = CommittedRepository("# Notes\n");
        repository.Write(ModuleInstructions, "# Notes\nChanged.\n");

        var run = GateHarness.Check(repository.Root);

        Assert.Equal(ExitCodes.FindingsReported, run.ExitCode);
        Assert.Contains($"{RuleIds.ModuleChangedWithoutDocs} {Module}/MODULE.md", run.Output, StringComparison.Ordinal);
    }

    [Fact]
    public void Deleting_a_generated_module_claude_md_still_needs_docs()
    {
        using var repository = CommittedRepository(GeneratedHeader + "# Rules\n");
        File.Delete(RepositoryPaths.ToFullPath(repository.Root, ModuleInstructions));

        Assert.Equal(ExitCodes.FindingsReported, GateHarness.Check(repository.Root).ExitCode);
    }

    [Fact]
    public void A_rules_change_that_regenerates_claude_md_still_needs_docs()
    {
        using var repository = CommittedRepository(GeneratedHeader + "# Rules\n- automated from P1\n");
        repository.Write(ModuleInstructions, GeneratedHeader + "# Rules\n- automated from P2\n");
        repository.Write($"{Module}/{RepositoryLayout.ModuleRulesFileName}", TempRepository.ModuleRules + "owner: records\n");

        var run = GateHarness.Check(repository.Root);

        Assert.Equal(ExitCodes.FindingsReported, run.ExitCode);
        Assert.Contains(RepositoryLayout.ModuleRulesFileName, run.Output, StringComparison.Ordinal);
    }

    [Fact]
    public void The_marker_must_open_the_file_and_the_file_must_be_named_claude_md()
    {
        using var repository = TempRepository.CreateFolder()
            .Write("a/CLAUDE.md", GeneratedHeader)
            .Write("b/CLAUDE.md", "# Intro\n" + GeneratedHeader)
            .Write("c/NOTES.md", GeneratedHeader);

        Assert.True(GeneratedModuleInstructions.IsGenerated(root: repository.Root, relativePath: "a/CLAUDE.md"));
        Assert.False(GeneratedModuleInstructions.IsGenerated(root: repository.Root, relativePath: "b/CLAUDE.md"));
        Assert.False(GeneratedModuleInstructions.IsGenerated(root: repository.Root, relativePath: "c/NOTES.md"));
        Assert.False(GeneratedModuleInstructions.IsGenerated(root: repository.Root, relativePath: "d/CLAUDE.md"));
    }

    private static TempRepository CommittedRepository(string moduleInstructions)
    {
        var repository = TempRepository.CreateGitRepository()
            .WriteModule(Module)
            .Write($"{Module}/Service.cs", "class Service { }\n")
            .Write(ModuleInstructions, moduleInstructions);
        repository.CommitAll("Initial");
        return repository;
    }
}
