namespace Codelab.Build.Analyzers.Tests.SdkFixture;

/// <summary>
/// A temp copy of Fixtures/SdkConsumer whose Directory.Build.props/.targets and Directory.Packages.props import the
/// repository's own files, so it builds exactly like a starter project: Codelab.Build.Sdk from source, the analyzer
/// project as an Analyzer, rules/codelab.globalconfig as the base thresholds. nuget.org is the only package source.
/// Shared by one test class (xunit runs its tests one after another), deleted at the end.
/// </summary>
public sealed class FixtureWorkspace : IDisposable
{
    private const string ProjectName = "Starter.SdkConsumer";
    private const string TempPrefix = "codelab-sdk-fixture-";
    private const string BuildConfiguration = "Debug";
    private const string TargetFramework = "net10.0";

    private static readonly string[] RepoImports = ["Directory.Build.props", "Directory.Build.targets", "Directory.Packages.props"];

    private const string NuGetConfig = """
        <?xml version="1.0" encoding="utf-8"?>
        <configuration>
          <packageSources>
            <clear />
            <add key="nuget.org" value="https://api.nuget.org/v3/index.json" />
          </packageSources>
        </configuration>
        """;

    public FixtureWorkspace()
    {
        Root = Path.Combine(Path.GetTempPath(), TempPrefix + Guid.NewGuid().ToString("N"));
        var fixture = Path.Combine(RepositoryRoot.Path, "packages", "dotnet", "Codelab.Build.Analyzers.Tests", "Fixtures", "SdkConsumer");
        ProjectDirectory = Path.Combine(Root, ProjectName);
        Directory.CreateDirectory(ProjectDirectory);
        foreach (var file in Directory.EnumerateFiles(fixture))
        {
            File.Copy(file, Path.Combine(ProjectDirectory, Path.GetFileName(file)));
        }

        foreach (var import in RepoImports)
        {
            File.WriteAllText(Path.Combine(Root, import), $"<Project>\n  <Import Project=\"{Path.Combine(RepositoryRoot.Path, import)}\" />\n</Project>\n");
        }

        File.WriteAllText(Path.Combine(Root, "nuget.config"), NuGetConfig);
    }

    public string Root { get; }

    public string ProjectDirectory { get; }

    public string ProjectPath => Path.Combine(ProjectDirectory, ProjectName + ".csproj");

    public string OutputAssemblyPath => Path.Combine(ProjectDirectory, "bin", BuildConfiguration, TargetFramework, ProjectName + ".dll");

    /// <summary>Adds a source file to the fixture project; dispose the result to remove it again.</summary>
    public IDisposable AddSource(string fileName, string content)
    {
        var path = Path.Combine(ProjectDirectory, fileName);
        File.WriteAllText(path, content);
        return new SourceFile(path);
    }

    public Task<DotnetResult> BuildAsync(params string[] properties) =>
        DotnetCli.BuildAsync(ProjectPath, new Dictionary<string, string>(), properties);

    public Task<DotnetResult> BuildAsync(IReadOnlyDictionary<string, string> environment, params string[] properties) =>
        DotnetCli.BuildAsync(ProjectPath, environment, properties);

    public void Dispose() => Directory.Delete(Root, recursive: true);

    private sealed class SourceFile(string path) : IDisposable
    {
        public void Dispose() => File.Delete(path);
    }
}
