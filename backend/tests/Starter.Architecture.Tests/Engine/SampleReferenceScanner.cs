using System.Reflection;
using System.Xml.Linq;

namespace Starter.Architecture.Tests.Engine;

/// <summary>A project file: its repository-relative path and its XML.</summary>
/// <param name="Path">The path relative to the repository root, with forward slashes.</param>
/// <param name="Xml">The project XML.</param>
public sealed record ProjectSource(string Path, string Xml);

/// <summary>
/// Who may reference the deletable sample module (build-plan phase 7, deletability): only the host and the sample's own
/// test project. Checked by assembly and project NAME, so this compiles and runs with or without the sample.
/// </summary>
public static class SampleReferenceScanner
{
    /// <summary>The sample module's assembly and project name.</summary>
    public const string SampleAssemblyName = "Starter.Modules.ServiceRequests";

    /// <summary>The host, which composes the sample into the API.</summary>
    public const string HostAssemblyName = "Starter.Api";

    /// <summary>The sample's own test project.</summary>
    public const string SampleTestsAssemblyName = SampleAssemblyName + ".Tests";

    /// <summary>The projects allowed to reference the sample (besides the sample itself).</summary>
    public static readonly IReadOnlySet<string> AllowedReferencers =
        new HashSet<string>([HostAssemblyName, SampleTestsAssemblyName], StringComparer.OrdinalIgnoreCase);

    /// <summary>The repository folders that hold .NET projects.</summary>
    public static readonly IReadOnlyList<string> ProjectFolders = ["backend", "services", "packages", "tools"];

    private const string ProjectPattern = "*.csproj";
    private const string ProjectReferenceElement = "ProjectReference";
    private const string PackageReferenceElement = "PackageReference";
    private const string IncludeAttribute = "Include";
    private static readonly string[] SkippedSegments = ["bin", "obj", "node_modules"];

    /// <summary>Every project file under <see cref="ProjectFolders"/> (build output excluded).</summary>
    public static IReadOnlyList<ProjectSource> ReadProjects(string repositoryRoot) =>
        [.. ProjectFolders
            .Select(folder => Path.Combine(repositoryRoot, folder))
            .Where(Directory.Exists)
            .SelectMany(folder => Directory.EnumerateFiles(folder, ProjectPattern, SearchOption.AllDirectories))
            .Select(path => Path.GetRelativePath(repositoryRoot, path).Replace('\\', '/'))
            .Where(relative => !relative.Split('/').Any(segment => SkippedSegments.Contains(segment, StringComparer.OrdinalIgnoreCase)))
            .Order(StringComparer.Ordinal)
            .Select(relative => new ProjectSource(relative, File.ReadAllText(Path.Combine(repositoryRoot, relative))))];

    /// <summary>The projects, other than the sample and the allowed referencers, that reference the sample (project or package).</summary>
    public static IReadOnlyList<string> ForbiddenProjectReferences(IEnumerable<ProjectSource> projects) =>
        [.. projects
            .Where(project => !IsAllowed(NameOf(project.Path)))
            .Where(project => References(project.Xml).Any(IsSample))
            .Select(project => project.Path)];

    /// <summary>The loaded assemblies, other than the sample and the allowed referencers, whose metadata references the sample.</summary>
    public static IReadOnlyList<string> ForbiddenAssemblyReferences(IEnumerable<Assembly> assemblies) =>
        [.. assemblies
            .Where(assembly => !IsAllowed(assembly.GetName().Name ?? string.Empty))
            .Where(assembly => assembly.GetReferencedAssemblies().Any(reference => IsSample(reference.Name ?? string.Empty)))
            .Select(assembly => assembly.GetName().Name ?? string.Empty)];

    private static bool IsAllowed(string name) =>
        AllowedReferencers.Contains(name) || string.Equals(name, SampleAssemblyName, StringComparison.OrdinalIgnoreCase);

    private static bool IsSample(string reference) =>
        string.Equals(NameOf(reference), SampleAssemblyName, StringComparison.OrdinalIgnoreCase);

    private static IEnumerable<string> References(string xml) =>
        XDocument.Parse(xml)
            .Descendants()
            .Where(element => element.Name.LocalName is ProjectReferenceElement or PackageReferenceElement)
            .Select(element => (string?)element.Attribute(IncludeAttribute))
            .OfType<string>();

    /// <summary>The project or assembly name of a path or include (<c>..\X\X.csproj</c> gives <c>X</c>; a bare name stays).</summary>
    private static string NameOf(string pathOrName)
    {
        var fileName = pathOrName.Replace('\\', '/').Split('/')[^1];
        return fileName.EndsWith(ProjectPattern[1..], StringComparison.OrdinalIgnoreCase)
            ? fileName[..^ProjectPattern[1..].Length]
            : fileName;
    }
}
