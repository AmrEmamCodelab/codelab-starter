using Starter.Tools.DocsGate.Checks;
using Starter.Tools.DocsGate.Cli;

namespace Starter.Tools.DocsGate.Tests;

public sealed class DeadLinkCheckTests
{
    private const string Document = "docs/guide.md";

    [Theory]
    [InlineData("[ok](other.md)")]
    [InlineData("[ok](./other.md#section)")]
    [InlineData("[ok](../README.md)")]
    [InlineData("[ok](/README.md)")]
    [InlineData("[ok](sub/)")]
    [InlineData("[ok](<sub/with space.md>)")]
    [InlineData("[ok](sub/with%20space.md)")]
    [InlineData("![img](sub/with%20space.md \"title\")")]
    [InlineData("[ok]: other.md")]
    [InlineData("<a href=\"other.md\">ok</a>")]
    [InlineData("[web](https://example.invalid/missing.md)")]
    [InlineData("[mail](mailto:someone@example.invalid)")]
    [InlineData("[anchor](#purpose)")]
    [InlineData("`[code](missing.md)`")]
    [InlineData("```\n[fenced](missing.md)\n```")]
    public void Live_or_non_local_links_pass(string markdown)
    {
        using var repository = CreateRepository(markdown);

        Assert.Empty(DeadLinkCheck.Check(repository.Root, [Document]));
    }

    [Theory]
    [InlineData("[dead](missing.md)")]
    [InlineData("[dead](missing.md#section)")]
    [InlineData("[dead](/docs/missing.md)")]
    [InlineData("![dead](img/missing.png)")]
    [InlineData("[dead]: ../nowhere/")]
    [InlineData("<img src=\"missing.png\">")]
    [InlineData("[wrong case](OTHER.md)")]
    public void Dead_links_fail_with_their_line(string markdown)
    {
        using var repository = CreateRepository("# Guide\n\n" + markdown);

        var finding = Assert.Single(DeadLinkCheck.Check(repository.Root, [Document]));

        Assert.Equal(RuleIds.DeadLink, finding.RuleId);
        Assert.Equal(Document, finding.Path);
        Assert.Equal(3, finding.Line);
    }

    [Fact]
    public void The_gate_fails_on_a_dead_link_anywhere_in_the_tree()
    {
        using var repository = CreateRepository("[dead](missing.md)");

        var run = GateHarness.Check(repository.Root);

        Assert.Equal(ExitCodes.FindingsReported, run.ExitCode);
        Assert.Contains($"{RuleIds.DeadLink} {Document}:1", run.Output, StringComparison.Ordinal);
    }

    [Fact]
    public void Markdown_in_excluded_folders_is_not_checked()
    {
        using var repository = TempRepository.CreateFolder().Write("node_modules/pkg/README.md", "[dead](missing.md)");

        Assert.Equal(ExitCodes.Passed, GateHarness.Check(repository.Root).ExitCode);
    }

    [Fact]
    public void Gitignored_markdown_is_not_checked_in_a_git_work_tree()
    {
        using var repository = TempRepository.CreateGitRepository()
            .Write(".gitignore", "scratch/\n")
            .Write("scratch/notes.md", "[dead](missing.md)");

        Assert.Equal(ExitCodes.Passed, GateHarness.Check(repository.Root).ExitCode);
    }

    private static TempRepository CreateRepository(string markdown) =>
        TempRepository.CreateFolder()
            .Write("README.md", "# Root\n")
            .Write("docs/other.md", "# Other\n")
            .Write("docs/sub/with space.md", "# Spaced\n")
            .Write(Document, markdown);
}
