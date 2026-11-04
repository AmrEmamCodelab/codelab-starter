using Codelab.Testing.Architecture;
using Codelab.Testing.Sweeps;
using Codelab.Testing.Tests.Support;

namespace Codelab.Testing.Tests.Sweeps;

public sealed class SourceSweepTests
{
    // Sample terms are neutral words; the repository's real term list is read by path in the last test.
    private const string TermText = """
        # sample term list
        legacy-brand/i: acme
        legacy-area/i: \bwarehouse\b
        """;

    [Fact]
    public void Term_rules_match_raw_and_normalized_lines()
    {
        var rules = SweepConfigurationFiles.ParseTermRules(TermText, "terms");

        var findings = SourceSweep.SweepText("var client = new AcmeClient();\nclass WarehouseQueue {}\nthe warehouse door\n", "src/a.cs", rules);

        Assert.Equal(["term-legacy-brand@1", "term-legacy-area@2", "term-legacy-area@3"], findings.Select(finding => $"{finding.RuleId}@{finding.Line}"));
    }

    [Fact]
    public void Allowances_let_a_rule_match_in_the_listed_paths_only()
    {
        var rules = SweepConfigurationFiles.ParseTermRules(TermText, "terms");
        var allowances = SweepConfigurationFiles.ParseExceptions("# reviewed\nprovenance/**  term-*\nnotes.md legacy-other\n", "allow");

        Assert.Empty(SourceSweep.SweepText("acme", "provenance/origins/a.json", rules, allowances));
        Assert.Single(SourceSweep.SweepText("acme", "docs/notes.md", rules, allowances));
        Assert.Equal(2, allowances.Count);
    }

    [Theory]
    [InlineData("no separator")]
    [InlineData("bad/x: a")]
    [InlineData("broken: (unclosed")]
    public void Malformed_term_lines_are_refused(string line) =>
        Assert.Throws<FormatException>(() => SweepConfigurationFiles.ParseTermRules(line, "terms"));

    [Fact]
    public void Malformed_exception_lines_are_refused() =>
        Assert.Throws<FormatException>(() => SweepConfigurationFiles.ParseExceptions("only-a-glob", "allow"));

    [Fact]
    public void A_tree_is_swept_with_extension_folder_and_path_filters()
    {
        using var workspace = new TempWorkspace();
        workspace.Write("src/Service.cs", "// acme\n");
        workspace.Write("src/readme.md", "acme\n");
        workspace.Write("src/bin/Debug/Copy.cs", "acme\n");
        workspace.Write("src/Generated.cs", "acme\n");
        workspace.Write("src/clean.cs", "fine\n");
        var options = new SourceSweepOptions { Root = workspace.Root, IncludeExtensions = [".cs"], ExcludedPaths = ["src/Generated.cs"] };
        var rules = SweepConfigurationFiles.ParseTermRules(TermText, "terms");

        var findings = SourceSweep.Run(options, rules);
        var check = SourceSweep.Check(options, rules);

        Assert.Equal("src/Service.cs:1: [term-legacy-brand] acme", Assert.Single(findings).ToString());
        Assert.Equal(2, check.Examined);
        Assert.False(check.IsSuccessful);
    }

    [Fact]
    public void Configuration_files_are_read_from_disk()
    {
        using var workspace = new TempWorkspace();
        var terms = workspace.Write("terms.txt", TermText);
        var allow = workspace.Write("allow.txt", "*.md *\n");

        Assert.Equal(2, SweepConfigurationFiles.ReadTermRules(terms).Count);
        Assert.Single(SweepConfigurationFiles.ReadExceptions(allow));
    }

    [Fact]
    public void The_package_folders_are_clean_under_the_repository_term_list()
    {
        var root = RepositoryRoot.Find(CompiledRules.RelativePath);
        var extractFolder = Path.Combine(root, "tools", "extract");
        var termFile = Directory.EnumerateFiles(extractFolder, "*-terms.txt").Single();
        var rules = SweepConfigurationFiles.ReadTermRules(termFile).Concat(SweepRules.Secrets()).ToList();

        foreach (var package in new[] { "Codelab.Testing", "Codelab.Testing.Tests" })
        {
            var options = new SourceSweepOptions { Root = Path.Combine(root, "packages", "dotnet", package) };
            SourceSweep.Check(options, rules).ThrowIfFailed();
        }
    }
}
