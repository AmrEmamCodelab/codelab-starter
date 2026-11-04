using Starter.Tools.DocsGate.Checks;
using Starter.Tools.DocsGate.Cli;

namespace Starter.Tools.DocsGate.Tests;

// Markers are built at runtime so this file never contains one (the repo's own marker scans would flag it).
public sealed class MergeMarkerTests
{
    private const int MarkerLength = 7;
    private static readonly string Start = new('<', MarkerLength);
    private static readonly string Base = new('|', MarkerLength);
    private static readonly string Separator = new('=', MarkerLength);
    private static readonly string End = new('>', MarkerLength);

    [Fact]
    public void A_conflict_block_reports_every_marker_line()
    {
        var text = $"intro\n{Start} HEAD\nours\n{Base} base\nold\n{Separator}\ntheirs\n{End} feature/x\n";

        var markers = MergeMarkerScanner.Scan(text).ToList();

        Assert.Equal([(2, Start), (4, Base), (6, Separator), (8, End)], markers);
    }

    [Fact]
    public void Crlf_line_endings_are_handled()
    {
        var text = $"{Start}\r\na\r\n{Separator}\r\nb\r\n{End}\r\n";

        Assert.Equal(3, MergeMarkerScanner.Scan(text).Count());
    }

    [Theory]
    [InlineData("Title\n=======\n")]
    [InlineData("text <<<<<<< in the middle\n")]
    [InlineData("<<<<<<<< eight is not a marker\n")]
    [InlineData("<<<<<<<HEAD\n")]
    public void Lookalikes_are_not_markers(string text)
    {
        Assert.Empty(MergeMarkerScanner.Scan(text));
    }

    [Fact]
    public void The_gate_fails_on_a_marker_in_markdown()
    {
        using var repository = TempRepository.CreateFolder().Write("docs/notes.md", $"# Notes\n{Start} HEAD\na\n{Separator}\nb\n{End} main\n");

        var run = GateHarness.Check(repository.Root);

        Assert.Equal(ExitCodes.FindingsReported, run.ExitCode);
        Assert.Contains($"{RuleIds.MergeMarker} docs/notes.md:2", run.Output, StringComparison.Ordinal);
    }

    [Fact]
    public void The_gate_fails_on_a_marker_in_module_rules()
    {
        using var repository = TempRepository.CreateFolder()
            .WriteModule("Records")
            .Write("Records/module.rules.yaml", $"{Start} HEAD\nmodule: a\n{Separator}\nmodule: b\n{End} main\n");

        var run = GateHarness.Check(repository.Root);

        Assert.Contains($"{RuleIds.MergeMarker} Records/module.rules.yaml:1", run.Output, StringComparison.Ordinal);
    }
}
