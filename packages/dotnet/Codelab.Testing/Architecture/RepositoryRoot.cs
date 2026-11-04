namespace Codelab.Testing.Architecture;

/// <summary>Finds the repository root by walking up from a start folder until a marker file exists.</summary>
public static class RepositoryRoot
{
    /// <summary>
    /// The first folder at or above <paramref name="startDirectory"/> (default: the test output folder) that holds
    /// <paramref name="markerRelativePath"/>, for example <see cref="CompiledRules.RelativePath"/>.
    /// </summary>
    /// <exception cref="DirectoryNotFoundException">No folder above the start holds the marker.</exception>
    public static string Find(string markerRelativePath, string? startDirectory = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(markerRelativePath);
        var start = startDirectory ?? AppContext.BaseDirectory;
        for (var directory = new DirectoryInfo(start); directory is not null; directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, markerRelativePath)))
            {
                return directory.FullName;
            }
        }

        throw new DirectoryNotFoundException($"No folder at or above '{start}' contains '{markerRelativePath}'.");
    }
}
