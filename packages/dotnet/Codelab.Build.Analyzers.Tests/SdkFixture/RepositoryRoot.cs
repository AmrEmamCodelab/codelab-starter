namespace Codelab.Build.Analyzers.Tests.SdkFixture;

/// <summary>The starter repository root: the first folder above the test binaries that holds the solution file.</summary>
internal static class RepositoryRoot
{
    private const string SolutionFileName = "codelab-starter.slnx";

    private static readonly Lazy<string> Root = new(Find);

    public static string Path => Root.Value;

    private static string Find()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            if (File.Exists(System.IO.Path.Combine(directory.FullName, SolutionFileName)))
            {
                return directory.FullName;
            }
        }

        throw new InvalidOperationException($"No {SolutionFileName} above {AppContext.BaseDirectory}; run the tests from the repository.");
    }
}
