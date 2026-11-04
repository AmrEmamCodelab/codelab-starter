using System.Xml.Linq;

namespace Codelab.Testing.Architecture;

/// <summary>
/// Maps a module name from the compiled rules to the root namespace of its code. The default reads the module's
/// project file (its <c>RootNamespace</c>, else the project file name), so a renamed fork needs no table.
/// </summary>
public sealed class ModuleNamespaceMap
{
    private const string ProjectFilePattern = "*.csproj";
    private const string RootNamespaceElement = "RootNamespace";

    private readonly Dictionary<string, string> _namespaces;

    private ModuleNamespaceMap(Dictionary<string, string> namespaces) => _namespaces = namespaces;

    /// <summary>Every mapped module and its root namespace.</summary>
    public IReadOnlyDictionary<string, string> Namespaces => _namespaces;

    /// <summary>A map from explicit pairs (module name → root namespace).</summary>
    public static ModuleNamespaceMap FromPairs(IEnumerable<KeyValuePair<string, string>> pairs)
    {
        ArgumentNullException.ThrowIfNull(pairs);
        return new ModuleNamespaceMap(new Dictionary<string, string>(pairs, StringComparer.Ordinal));
    }

    /// <summary>
    /// A map built from each .NET module's project file under <paramref name="repositoryRoot"/>. A module folder without
    /// a project file is left out (it has no code yet).
    /// </summary>
    public static ModuleNamespaceMap FromProjects(CompiledRules rules, string repositoryRoot)
    {
        ArgumentNullException.ThrowIfNull(rules);
        ArgumentException.ThrowIfNullOrWhiteSpace(repositoryRoot);
        var namespaces = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var module in rules.DotnetModules)
        {
            var rootNamespace = ReadRootNamespace(Path.Combine(repositoryRoot, module.Path));
            if (rootNamespace is not null)
            {
                namespaces[module.Module] = rootNamespace;
            }
        }

        return new ModuleNamespaceMap(namespaces);
    }

    /// <summary>The root namespace of a module, or null when it is not mapped.</summary>
    public string? NamespaceOf(string module) => _namespaces.GetValueOrDefault(module);

    private static string? ReadRootNamespace(string moduleDirectory)
    {
        if (!Directory.Exists(moduleDirectory))
        {
            return null;
        }

        var project = Directory.EnumerateFiles(moduleDirectory, ProjectFilePattern, SearchOption.TopDirectoryOnly)
            .OrderBy(path => path, StringComparer.Ordinal)
            .FirstOrDefault();
        if (project is null)
        {
            return null;
        }

        var declared = XDocument.Load(project).Descendants(RootNamespaceElement).Select(element => element.Value.Trim()).FirstOrDefault();
        return string.IsNullOrEmpty(declared) ? Path.GetFileNameWithoutExtension(project) : declared;
    }
}
