using Starter.Tools.DocsGate.Checks;

namespace Starter.Tools.DocsGate.Tests;

public sealed class ModuleChangeCheckTests
{
    private static readonly string[] Modules = ["src/Records", "src/Records/Inner", "src/Users"];

    [Fact]
    public void A_module_change_without_a_doc_change_fails()
    {
        var findings = Check(["src/Records/Service.cs"], reason: null, out _);

        var finding = Assert.Single(findings);
        Assert.Equal(RuleIds.ModuleChangedWithoutDocs, finding.RuleId);
        Assert.Equal("src/Records/MODULE.md", finding.Path);
        Assert.Contains("src/Records/Service.cs", finding.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void A_module_change_with_a_doc_change_passes()
    {
        Assert.Empty(Check(["src/Records/Service.cs", "src/Records/MODULE.md"], reason: null, out _));
    }

    [Fact]
    public void The_override_reason_lets_the_change_through_and_is_reported()
    {
        var findings = Check(["src/Records/Service.cs"], reason: "rename only", out var notices);

        Assert.Empty(findings);
        var notice = Assert.Single(notices);
        Assert.Contains("rename only", notice, StringComparison.Ordinal);
    }

    [Fact]
    public void A_file_belongs_to_its_deepest_module()
    {
        var findings = Check(["src/Records/Inner/Thing.cs", "src/Records/MODULE.md"], reason: null, out _);

        Assert.Equal("src/Records/Inner/MODULE.md", Assert.Single(findings).Path);
    }

    [Fact]
    public void Changes_outside_modules_and_in_lookalike_folders_are_ignored()
    {
        Assert.Empty(Check(["README.md", "src/RecordsArchive/x.cs", "src/Shared/y.cs"], reason: null, out _));
    }

    [Fact]
    public void Long_change_lists_are_summarised()
    {
        var findings = Check(["src/Users/a.cs", "src/Users/b.cs", "src/Users/c.cs", "src/Users/d.cs", "src/Users/e.cs"], reason: null, out _);

        Assert.Contains("and 2 more", Assert.Single(findings).Message, StringComparison.Ordinal);
    }

    private static IReadOnlyList<Finding> Check(string[] changedFiles, string? reason, out List<string> notices)
    {
        notices = [];
        return ModuleChangeCheck.Check(
            moduleDirectories: Modules,
            changedFiles: changedFiles,
            docsNotNeededReason: reason,
            notices: notices);
    }
}
