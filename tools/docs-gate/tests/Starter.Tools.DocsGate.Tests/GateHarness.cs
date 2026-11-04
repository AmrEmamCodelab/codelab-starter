using Starter.Tools.DocsGate.Cli;

namespace Starter.Tools.DocsGate.Tests;

/// <summary>Runs the CLI exactly as CI does, with a controlled environment and captured output.</summary>
public static class GateHarness
{
    public sealed record GateRun(int ExitCode, string Output, string Error);

    public static GateRun Check(
        string root,
        string? baseRef = null,
        IReadOnlyDictionary<string, string>? environment = null)
    {
        var arguments = new List<string> { CommandLineParser.CheckCommand, CommandLineParser.RootOption, root };
        if (baseRef is not null)
        {
            arguments.AddRange([CommandLineParser.BaseOption, baseRef]);
        }

        using var output = new StringWriter();
        using var error = new StringWriter();
        var exitCode = DocsGateCli.Run(
            arguments: arguments,
            output: output,
            error: error,
            readEnvironment: name => environment is not null && environment.TryGetValue(name, out var value) ? value : null);
        return new GateRun(exitCode, output.ToString(), error.ToString());
    }
}
