using Starter.Tools.DocsGate.Checks;
using Starter.Tools.DocsGate.Cli;

namespace Starter.Tools.DocsGate.Tests;

public sealed class ModuleDocsCheckTests
{
    [Fact]
    public void A_module_without_MODULE_md_fails()
    {
        using var repository = TempRepository.CreateFolder().WriteModule("src/Modules/Records", moduleDoc: null);

        var run = GateHarness.Check(repository.Root);

        Assert.Equal(ExitCodes.FindingsReported, run.ExitCode);
        Assert.Contains($"{RuleIds.ModuleDocMissing} src/Modules/Records/MODULE.md", run.Output, StringComparison.Ordinal);
    }

    [Fact]
    public void A_module_with_all_sections_passes()
    {
        using var repository = TempRepository.CreateFolder().WriteModule("src/Modules/Records");

        var run = GateHarness.Check(repository.Root);

        Assert.Equal(ExitCodes.Passed, run.ExitCode);
    }

    [Fact]
    public void Each_missing_section_is_reported()
    {
        const string partialDoc = "# Records\n\n## Purpose\nx\n\n### use\nx\n\n## Rules\nx\n";
        using var repository = TempRepository.CreateFolder().WriteModule("Records", partialDoc);

        var findings = ModuleDocsCheck.Check(repository.Root, ["Records"]).ToList();

        Assert.All(findings, finding => Assert.Equal(RuleIds.ModuleDocSectionMissing, finding.RuleId));
        Assert.Collection(
            findings,
            finding => Assert.Contains("'Extend'", finding.Message, StringComparison.Ordinal),
            finding => Assert.Contains("'Gotchas'", finding.Message, StringComparison.Ordinal));
    }

    [Fact]
    public void A_heading_inside_a_code_fence_does_not_count()
    {
        const string fencedDoc = "## Purpose\n## Use\n## Extend\n## Rules\n```md\n## Gotchas\n```\n";
        using var repository = TempRepository.CreateFolder().WriteModule("Records", fencedDoc);

        var findings = ModuleDocsCheck.Check(repository.Root, ["Records"]).ToList();

        var finding = Assert.Single(findings);
        Assert.Contains("'Gotchas'", finding.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void A_wrongly_cased_module_doc_counts_as_missing()
    {
        using var repository = TempRepository.CreateFolder().WriteModule("Records", moduleDoc: null);
        repository.Write("Records/module.md", TempRepository.FullModuleDoc);

        var finding = Assert.Single(ModuleDocsCheck.Check(repository.Root, ["Records"]));

        Assert.Equal(RuleIds.ModuleDocMissing, finding.RuleId);
    }
}
