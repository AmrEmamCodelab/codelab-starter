using Starter.Tools.DocsGate.Checks;
using Starter.Tools.DocsGate.Repository;

namespace Starter.Tools.DocsGate.Cli;

/// <summary>Entry point logic, kept out of Program.cs so tests can drive it with fake streams and environment.</summary>
internal static class DocsGateCli
{
    public static int Run(
        IReadOnlyList<string> arguments,
        TextWriter output,
        TextWriter error,
        Func<string, string?> readEnvironment)
    {
        var (options, parseError) = CommandLineParser.Parse(arguments);
        if (options is null)
        {
            error.WriteLine(parseError);
            error.WriteLine(CommandLineParser.Usage);
            return ExitCodes.UsageOrToolError;
        }

        if (!Directory.Exists(options.Root))
        {
            error.WriteLine($"Root '{options.Root}' does not exist.");
            return ExitCodes.UsageOrToolError;
        }

        try
        {
            var report = new DocsGateRunner(new GitClient(), readEnvironment).Run(options);
            return Print(report, output);
        }
        catch (GitCommandException exception)
        {
            error.WriteLine($"docs gate: {exception.Message}");
            return ExitCodes.UsageOrToolError;
        }
    }

    private static int Print(GateReport report, TextWriter output)
    {
        foreach (var notice in report.Notices)
        {
            output.WriteLine($"note: {notice}");
        }

        foreach (var finding in report.Findings)
        {
            output.WriteLine(finding.ToString());
        }

        if (report.Findings.Count == 0)
        {
            output.WriteLine("docs gate: PASSED");
            return ExitCodes.Passed;
        }

        output.WriteLine($"docs gate: FAILED with {report.Findings.Count} finding(s)");
        return ExitCodes.FindingsReported;
    }
}
