using System.ComponentModel;
using System.Diagnostics;
using System.Text;

namespace Starter.Tools.DocsGate.Repository;

/// <summary>Runs the git executable on PATH.</summary>
internal sealed class GitClient : IGitClient
{
    private const string GitExecutable = "git";
    private const string TrueOutput = "true";

    public bool IsWorkTree(string directory)
    {
        try
        {
            var result = Run(directory, ["rev-parse", "--is-inside-work-tree"]);
            return result.Succeeded && string.Equals(result.Output.Trim(), TrueOutput, StringComparison.Ordinal);
        }
        catch (Win32Exception)
        {
            // git is not installed: the tree is treated as a plain folder.
            return false;
        }
    }

    public GitResult Run(string directory, IReadOnlyList<string> arguments)
    {
        var startInfo = new ProcessStartInfo(GitExecutable)
        {
            WorkingDirectory = directory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
        };
        foreach (var argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        using var process = Process.Start(startInfo)
            ?? throw new GitCommandException($"Could not start '{GitExecutable}'.");

        // Read stderr concurrently so a full stderr pipe cannot block the process while stdout is drained.
        var errorTask = process.StandardError.ReadToEndAsync();
        var output = process.StandardOutput.ReadToEnd();
        process.WaitForExit();
        return new GitResult(process.ExitCode, output, errorTask.GetAwaiter().GetResult());
    }
}
