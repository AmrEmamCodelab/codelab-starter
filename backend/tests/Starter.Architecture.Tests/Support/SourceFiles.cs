namespace Starter.Architecture.Tests.Support;

/// <summary>One C# source file under <c>backend/src</c>, with the module (or the kernel) that owns it.</summary>
/// <param name="Name">The path relative to <c>backend/src</c>, used in finding locations.</param>
/// <param name="Owner">The module name (the folder under <c>Modules/</c>), or <see cref="SourceFiles.KernelOwner"/>.</param>
/// <param name="Text">The source text.</param>
public sealed record OwnedSource(string Name, string Owner, string Text);

/// <summary>The backend source files, read from disk with their owners (build output excluded).</summary>
public static class SourceFiles
{
    /// <summary>The owner of every file outside <c>Modules/</c> (the host and the kernel projects).</summary>
    public const string KernelOwner = "Kernel";

    /// <summary>The backend source folder, relative to the repository root.</summary>
    public const string BackendSourceFolder = "backend/src";

    private const string SourcePattern = "*.cs";
    private const string ModulesFolder = "Modules";
    private const int ModuleSegmentIndex = 1;
    private static readonly string[] BuildOutputSegments = ["bin", "obj"];
    private static readonly char[] Separators = [Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar];

    /// <summary>Every C# file under <c>backend/src</c> of the repository.</summary>
    public static IReadOnlyList<OwnedSource> ReadBackend() =>
        Read(Path.Combine(StarterSolution.RepositoryRootPath, BackendSourceFolder));

    /// <summary>Every C# file under <paramref name="directory"/>, with its owner.</summary>
    public static IReadOnlyList<OwnedSource> Read(string directory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(directory);
        return [.. Directory.EnumerateFiles(directory, SourcePattern, SearchOption.AllDirectories)
            .Select(path => Path.GetRelativePath(directory, path))
            .Where(relative => !Segments(relative).Any(segment => BuildOutputSegments.Contains(segment, StringComparer.OrdinalIgnoreCase)))
            .Order(StringComparer.Ordinal)
            .Select(relative => new OwnedSource(relative, OwnerOf(relative), File.ReadAllText(Path.Combine(directory, relative))))];
    }

    /// <summary>The owner of a path relative to <c>backend/src</c>: the module folder name, or the kernel.</summary>
    public static string OwnerOf(string relativePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(relativePath);
        var segments = Segments(relativePath);
        return segments.Length > ModuleSegmentIndex + 1 && string.Equals(segments[0], ModulesFolder, StringComparison.Ordinal)
            ? segments[ModuleSegmentIndex]
            : KernelOwner;
    }

    private static string[] Segments(string relativePath) => relativePath.Split(Separators, StringSplitOptions.RemoveEmptyEntries);
}
