using System.Runtime.CompilerServices;

namespace Codelab.Integration.Tests.Support;

/// <summary>
/// Finds the repository root (the folder holding the solution file) from the test output folder, or, when the build
/// output lives outside the repository (<c>--artifacts-path</c>), from this source file's compile-time path.
/// </summary>
internal static class RepositoryPaths
{
    private const string SolutionFileName = "codelab-starter.slnx";
    private static readonly string[] BuildOutputSegments = ["bin", "obj"];

    public static string Root { get; } = FindRoot();

    public static bool IsBuildOutput(string path) =>
        path.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar).Any(segment => BuildOutputSegments.Contains(segment, StringComparer.OrdinalIgnoreCase));

    private static string FindRoot() =>
        FindAbove(AppContext.BaseDirectory)
        ?? FindAbove(Path.GetDirectoryName(SourceFilePath()))
        ?? throw new InvalidOperationException($"No {SolutionFileName} above {AppContext.BaseDirectory} or the test sources.");

    private static string? FindAbove(string? start)
    {
        for (var directory = start is null ? null : new DirectoryInfo(start); directory is not null; directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, SolutionFileName)))
            {
                return directory.FullName;
            }
        }

        return null;
    }

    private static string SourceFilePath([CallerFilePath] string path = "") => path;
}
