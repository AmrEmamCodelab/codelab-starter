using System.Text.Json;

namespace Codelab.Testing.Architecture;

/// <summary>One module of <c>rules/rules.compiled.json</c>: where it lives and which modules it may and must not use.</summary>
/// <param name="Module">The PascalCase module name, for example <c>CodelabSecurity</c> or <c>Records</c>.</param>
/// <param name="Kind"><c>dotnet</c> or <c>angular</c>.</param>
/// <param name="Path">The repo-relative module folder.</param>
/// <param name="MayDependOn">The modules this module may reference.</param>
/// <param name="MustNotDependOn">The modules this module must never reference.</param>
public sealed record CompiledModule(
    string Module,
    string Kind,
    string Path,
    IReadOnlyList<string> MayDependOn,
    IReadOnlyList<string> MustNotDependOn)
{
    /// <summary>True for a .NET module (the only kind the architecture tests check).</summary>
    public bool IsDotnet => string.Equals(Kind, CompiledRules.DotnetKind, StringComparison.Ordinal);
}

/// <summary>
/// The module part of <c>rules/rules.compiled.json</c>, the file the rules engine generates from every
/// <c>module.rules.yaml</c>. The architecture tests read it so the rules have one source.
/// </summary>
public sealed class CompiledRules
{
    /// <summary>The compiled rules file, relative to the repository root.</summary>
    public const string RelativePath = "rules/rules.compiled.json";

    /// <summary>The <c>kind</c> value of a .NET module.</summary>
    public const string DotnetKind = "dotnet";

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private CompiledRules(int schemaVersion, IReadOnlyList<CompiledModule> modules)
    {
        SchemaVersion = schemaVersion;
        Modules = modules;
    }

    /// <summary>The schema version of the compiled file.</summary>
    public int SchemaVersion { get; }

    /// <summary>Every module, in file order.</summary>
    public IReadOnlyList<CompiledModule> Modules { get; }

    /// <summary>The .NET modules.</summary>
    public IEnumerable<CompiledModule> DotnetModules => Modules.Where(module => module.IsDotnet);

    /// <summary>The module with this name.</summary>
    /// <exception cref="KeyNotFoundException">No module has this name.</exception>
    public CompiledModule Get(string module) =>
        Modules.FirstOrDefault(candidate => string.Equals(candidate.Module, module, StringComparison.Ordinal))
        ?? throw new KeyNotFoundException($"Module '{module}' is not in the compiled rules.");

    /// <summary>Reads a compiled rules file.</summary>
    public static CompiledRules Load(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        return Parse(File.ReadAllText(path), path);
    }

    /// <summary>Reads <see cref="RelativePath"/> from the repository that contains <paramref name="startDirectory"/>.</summary>
    public static CompiledRules LoadFromRepository(string? startDirectory = null) =>
        Load(System.IO.Path.Combine(RepositoryRoot.Find(RelativePath, startDirectory), RelativePath));

    /// <summary>Parses compiled rules JSON; <paramref name="sourceName"/> names the input in error messages.</summary>
    public static CompiledRules Parse(string json, string sourceName)
    {
        ArgumentNullException.ThrowIfNull(json);
        var document = JsonSerializer.Deserialize<CompiledRulesDocument>(json, JsonOptions)
            ?? throw new InvalidDataException($"{sourceName}: empty compiled rules.");
        var modules = (document.Modules ?? []).Select(module => module.ToModule(sourceName)).ToList();
        return new CompiledRules(document.SchemaVersion, modules);
    }

    private sealed record CompiledRulesDocument(int SchemaVersion, List<CompiledModuleDocument>? Modules);

    private sealed record CompiledModuleDocument(
        string? Module,
        string? Kind,
        string? Path,
        List<string>? MayDependOn,
        List<string>? MustNotDependOn)
    {
        public CompiledModule ToModule(string sourceName)
        {
            if (string.IsNullOrWhiteSpace(Module) || string.IsNullOrWhiteSpace(Kind) || string.IsNullOrWhiteSpace(Path))
            {
                throw new InvalidDataException($"{sourceName}: every module needs module, kind and path.");
            }

            return new CompiledModule(Module, Kind, Path, MayDependOn ?? [], MustNotDependOn ?? []);
        }
    }
}
