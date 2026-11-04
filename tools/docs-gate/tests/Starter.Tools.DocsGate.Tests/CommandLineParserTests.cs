using Starter.Tools.DocsGate.Cli;

namespace Starter.Tools.DocsGate.Tests;

public sealed class CommandLineParserTests
{
    [Fact]
    public void Check_with_root_and_base_is_parsed()
    {
        var (options, error) = CommandLineParser.Parse(["check", "--root", "some/dir", "--base", "origin/main"]);

        Assert.Null(error);
        Assert.Equal(Path.GetFullPath("some/dir"), options!.Root);
        Assert.Equal("origin/main", options.BaseRef);
    }

    [Fact]
    public void Root_defaults_to_the_current_directory()
    {
        var (options, _) = CommandLineParser.Parse(["check"]);

        Assert.Equal(Path.GetFullPath("."), options!.Root);
        Assert.Null(options.BaseRef);
    }

    [Theory]
    [InlineData]
    [InlineData("lint")]
    [InlineData("check", "--root")]
    [InlineData("check", "--colour", "red")]
    public void Invalid_arguments_are_rejected(params string[] arguments)
    {
        var (options, error) = CommandLineParser.Parse(arguments);

        Assert.Null(options);
        Assert.False(string.IsNullOrEmpty(error));
    }

    [Fact]
    public void Invalid_arguments_exit_with_the_usage_code()
    {
        using var output = new StringWriter();
        using var error = new StringWriter();

        var exitCode = DocsGateCli.Run(["lint"], output, error, _ => null);

        Assert.Equal(ExitCodes.UsageOrToolError, exitCode);
        Assert.Contains("Usage:", error.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void A_missing_root_exits_with_the_usage_code()
    {
        var run = GateHarness.Check(Path.Combine(Path.GetTempPath(), "docs-gate-missing-" + Guid.NewGuid().ToString("N")));

        Assert.Equal(ExitCodes.UsageOrToolError, run.ExitCode);
    }
}
