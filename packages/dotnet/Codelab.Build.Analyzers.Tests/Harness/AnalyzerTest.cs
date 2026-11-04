using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Codelab.Build.Analyzers.Tests.Harness;

/// <summary>
/// Compiles C# source against the running .NET's own assemblies and runs one analyzer with the given options.
/// The source must compile cleanly, so a test can never pass because its input was broken.
/// </summary>
internal sealed class AnalyzerTest(DiagnosticAnalyzer analyzer)
{
    private const string DefaultAssemblyName = "Starter.Sample";
    private const string SourcePath = "Sample.cs";
    private const string TrustedAssembliesKey = "TRUSTED_PLATFORM_ASSEMBLIES";

    private static readonly Lazy<ImmutableArray<MetadataReference>> References = new(LoadRuntimeReferences);

    private readonly Dictionary<string, string> _treeOptions = [];
    private readonly Dictionary<string, string> _globalOptions = [];
    private string _assemblyName = DefaultAssemblyName;

    public AnalyzerTest WithOption(string key, string value)
    {
        _treeOptions[key] = value;
        return this;
    }

    public AnalyzerTest WithGlobalOption(string key, string value)
    {
        _globalOptions[key] = value;
        return this;
    }

    public AnalyzerTest WithAssemblyName(string assemblyName)
    {
        _assemblyName = assemblyName;
        return this;
    }

    /// <summary>The analyzer's diagnostics, ordered by position.</summary>
    public async Task<ImmutableArray<Diagnostic>> RunAsync(string source)
    {
        var tree = CSharpSyntaxTree.ParseText(source, path: SourcePath);
        var compilation = CSharpCompilation.Create(
            _assemblyName,
            [tree],
            References.Value,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, nullableContextOptions: NullableContextOptions.Enable));
        AssertCompiles(compilation);

        var provider = new DictionaryOptionsProvider(new DictionaryOptions(_treeOptions), new DictionaryOptions(_globalOptions));
        var options = new AnalyzerOptions([], provider);
        var diagnostics = await compilation.WithAnalyzers([analyzer], options).GetAnalyzerDiagnosticsAsync();
        return [.. diagnostics.OrderBy(diagnostic => diagnostic.Location.SourceSpan.Start)];
    }

    private static void AssertCompiles(Compilation compilation)
    {
        var errors = compilation.GetDiagnostics().Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error).ToList();
        Assert.True(errors.Count == 0, "The test source does not compile:\n" + string.Join('\n', errors));
    }

    private static ImmutableArray<MetadataReference> LoadRuntimeReferences()
    {
        var paths = ((string)AppContext.GetData(TrustedAssembliesKey)!).Split(Path.PathSeparator);
        return [.. paths.Select(path => (MetadataReference)MetadataReference.CreateFromFile(path))];
    }
}
