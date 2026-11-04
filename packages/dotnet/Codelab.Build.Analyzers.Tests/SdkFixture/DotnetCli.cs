using System.Diagnostics;
using System.Text.RegularExpressions;

namespace Codelab.Build.Analyzers.Tests.SdkFixture;

/// <summary>The result of one dotnet invocation: exit code, combined output and the distinct error ids it printed.</summary>
public sealed partial record DotnetResult(int ExitCode, string Output)
{
    public IReadOnlySet<string> ErrorIds =>
        ErrorIdPattern().Matches(Output).Select(match => match.Groups["id"].Value).ToHashSet(StringComparer.Ordinal);

    [GeneratedRegex(@": error (?<id>[A-Z]+\d+):")]
    private static partial Regex ErrorIdPattern();
}

/// <summary>
/// Runs the .NET 10 SDK the way tools/build.ps1 resolves it: CODELAB_DOTNET, else the private install, else the host
/// that runs these tests (DOTNET_HOST_PATH), else dotnet on PATH. MSBuild variables of the parent build are removed so
/// the child build starts clean; node reuse and the compiler server are off so no process outlives the test.
/// </summary>
internal static class DotnetCli
{
    private const string OverrideVariable = "CODELAB_DOTNET";
    private const string HostPathVariable = "DOTNET_HOST_PATH";
    private const string PrivateDotnet = @"E:\codelab-tools\dotnet10\dotnet.exe";
    private const string PathDotnet = "dotnet";
    private const string MsBuildVariablePrefix = "MSBUILD";

    private static readonly TimeSpan Timeout = TimeSpan.FromMinutes(5);

    public static async Task<DotnetResult> BuildAsync(string projectPath, IReadOnlyDictionary<string, string> environment, params string[] properties)
    {
        string[] arguments = ["build", projectPath, "-warnaserror", "-nologo", "-v:minimal", "-nodeReuse:false", "-p:UseSharedCompilation=false", .. properties];
        var start = new ProcessStartInfo(Resolve(), arguments)
        {
            WorkingDirectory = Path.GetDirectoryName(projectPath)!,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        PrepareEnvironment(start.Environment, environment);
        using var process = Process.Start(start)!;
        using var timeout = new CancellationTokenSource(Timeout);
        var output = process.StandardOutput.ReadToEndAsync(timeout.Token);
        var error = process.StandardError.ReadToEndAsync(timeout.Token);
        await process.WaitForExitAsync(timeout.Token);
        return new DotnetResult(process.ExitCode, await output + await error);
    }

    private static string Resolve()
    {
        var candidates = new[] { Environment.GetEnvironmentVariable(OverrideVariable), PrivateDotnet, Environment.GetEnvironmentVariable(HostPathVariable) };
        return candidates.FirstOrDefault(candidate => !string.IsNullOrEmpty(candidate) && File.Exists(candidate)) ?? PathDotnet;
    }

    private static void PrepareEnvironment(IDictionary<string, string?> target, IReadOnlyDictionary<string, string> overrides)
    {
        foreach (var name in target.Keys.Where(key => key.StartsWith(MsBuildVariablePrefix, StringComparison.OrdinalIgnoreCase)).ToList())
        {
            target.Remove(name);
        }

        foreach (var (name, value) in overrides)
        {
            target[name] = value;
        }
    }
}
