namespace Codelab.Integration.Tests.Support;

/// <summary>Finds the repository root (the folder holding the solution file) from the test output folder.</summary>
internal static class RepositoryPaths
{
    private const string SolutionFileName = "codelab-starter.slnx";
    private static readonly string[] BuildOutputSegments = ["bin", "obj"];

    public static string Root { get; } = FindRoot();

    public static bool IsBuildOutput(string path) =>
        path.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar).Any(segment => BuildOutputSegments.Contains(segment, StringComparer.OrdinalIgnoreCase));

    private static string FindRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, SolutionFileName)))
            {
                return directory.FullName;
            }
        }

        throw new InvalidOperationException($"No {SolutionFileName} above {AppContext.BaseDirectory}.");
    }
}
