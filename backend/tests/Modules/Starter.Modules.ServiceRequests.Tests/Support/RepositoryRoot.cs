using System.Reflection;

namespace Starter.Modules.ServiceRequests.Tests.Support;

/// <summary>
/// The repository root: the first folder holding the solution file above the test output, else above this project's
/// source folder (baked in at build time), so the tests also work when built with <c>--artifacts-path</c> elsewhere.
/// </summary>
public static class RepositoryRoot
{
    private const string Marker = "codelab-starter.slnx";
    private const string SourceDirectoryKey = "SourceProjectDirectory";

    /// <summary>The root folder.</summary>
    /// <exception cref="InvalidOperationException">No solution file is found above either start.</exception>
    public static string Find()
    {
        var source = typeof(RepositoryRoot).Assembly.GetCustomAttributes<AssemblyMetadataAttribute>()
            .FirstOrDefault(attribute => attribute.Key == SourceDirectoryKey)?.Value;
        foreach (var start in new[] { AppContext.BaseDirectory, source }.OfType<string>())
        {
            for (var directory = new DirectoryInfo(start); directory is not null; directory = directory.Parent)
            {
                if (File.Exists(Path.Combine(directory.FullName, Marker)))
                {
                    return directory.FullName;
                }
            }
        }

        throw new InvalidOperationException($"No {Marker} above {AppContext.BaseDirectory} or {source}.");
    }
}
