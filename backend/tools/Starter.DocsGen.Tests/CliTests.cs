using Starter.DocsGen.Cli;
using Starter.DocsGen.Rendering;
using Starter.DocsGen.Tests.Fixtures;

namespace Starter.DocsGen.Tests;

public sealed class ArgumentTests
{
    [Fact]
    public void Out_alone_writes()
    {
        var parsed = DocsGenArguments.Parse(["--out", "docs/generated"]);

        Assert.Equal(new DocsGenArguments("docs/generated", Check: false), parsed.Arguments);
    }

    [Fact]
    public void Check_in_any_position_checks()
    {
        var parsed = DocsGenArguments.Parse(["--check", "--out", "x"]);

        Assert.Equal(new DocsGenArguments("x", Check: true), parsed.Arguments);
    }

    [Theory]
    [InlineData("")]
    [InlineData("--check")]
    [InlineData("--out")]
    [InlineData("--out --check")]
    [InlineData("--out a --out b")]
    [InlineData("--out a --check --check")]
    [InlineData("--out a --unknown")]
    public void Bad_command_lines_are_usage_errors(string commandLine)
    {
        var parsed = DocsGenArguments.Parse(commandLine.Split(' ', StringSplitOptions.RemoveEmptyEntries));

        Assert.Null(parsed.Arguments);
        Assert.False(string.IsNullOrWhiteSpace(parsed.Error));
    }

    [Fact]
    public void A_blank_folder_is_a_usage_error()
    {
        Assert.Null(DocsGenArguments.Parse(["--out", "  "]).Arguments);
    }

    [Fact]
    public void Help_is_a_help_request()
    {
        Assert.True(DocsGenArguments.Parse(["--help"]).HelpRequested);
    }
}

public sealed class CommandTests
{
    [Fact]
    public async Task Writes_then_checks_clean()
    {
        using var folder = new TempFolder();
        var source = new FixedSurfaceSource(SurfaceFixtures.Surface());

        var written = await Run(source, "--out", folder.Path);
        var (checkedCode, output, _) = await RunCapturing(source, "--out", folder.Path, "--check");

        Assert.Equal(DocsGenExitCode.Success, written);
        Assert.Equal(DocsGenExitCode.Success, checkedCode);
        Assert.Contains("OK", output, StringComparison.Ordinal);
        Assert.True(File.Exists(folder.File(GeneratedFileNames.PermissionsJson)));
    }

    [Fact]
    public async Task Check_lists_every_drifting_file_and_exits_1()
    {
        using var folder = new TempFolder();
        var source = new FixedSurfaceSource(SurfaceFixtures.Surface());
        await Run(source, "--out", folder.Path);
        await File.WriteAllTextAsync(folder.File(GeneratedFileNames.Endpoints), "hand edit");
        File.Delete(folder.File(GeneratedFileNames.ErrorCodes));

        var (code, _, error) = await RunCapturing(source, "--out", folder.Path, "--check");

        Assert.Equal(DocsGenExitCode.Drift, code);
        Assert.Contains("changed: " + GeneratedFileNames.Endpoints, error, StringComparison.Ordinal);
        Assert.Contains("missing: " + GeneratedFileNames.ErrorCodes, error, StringComparison.Ordinal);
        Assert.Contains(GeneratedDocSet.RegenerateCommand, error, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Check_never_writes()
    {
        using var folder = new TempFolder();

        var code = await Run(new FixedSurfaceSource(SurfaceFixtures.Surface()), "--out", folder.Path, "--check");

        Assert.Equal(DocsGenExitCode.Drift, code);
        Assert.False(Directory.Exists(folder.Path));
    }

    [Fact]
    public async Task Usage_error_exits_2_without_reading_the_host()
    {
        var source = new FixedSurfaceSource(SurfaceFixtures.Surface());

        var (code, _, error) = await RunCapturing(source, "--nope");

        Assert.Equal(DocsGenExitCode.Usage, code);
        Assert.Contains(DocsGenArguments.Usage, error, StringComparison.Ordinal);
        Assert.Equal(0, source.Calls);
    }

    [Fact]
    public async Task Help_exits_0()
    {
        var (code, output, _) = await RunCapturing(new FixedSurfaceSource(SurfaceFixtures.Surface()), "--help");

        Assert.Equal(DocsGenExitCode.Success, code);
        Assert.Contains(DocsGenArguments.Usage, output, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_host_that_cannot_be_read_exits_3()
    {
        using var folder = new TempFolder();
        var source = new FixedSurfaceSource(surface: null, new InvalidOperationException("no host"));

        var (code, _, error) = await RunCapturing(source, "--out", folder.Path);

        Assert.Equal(DocsGenExitCode.Failure, code);
        Assert.Contains("no host", error, StringComparison.Ordinal);
        Assert.False(Directory.Exists(folder.Path));
    }

    private static async Task<DocsGenExitCode> Run(FixedSurfaceSource source, params string[] arguments) =>
        (await RunCapturing(source, arguments)).Code;

    private static async Task<(DocsGenExitCode Code, string Output, string Error)> RunCapturing(FixedSurfaceSource source, params string[] arguments)
    {
        using var output = new StringWriter();
        using var error = new StringWriter();
        var code = await DocsGenCommand.RunAsync(arguments, source, output, error, CancellationToken.None);
        return (code, output.ToString(), error.ToString());
    }
}
