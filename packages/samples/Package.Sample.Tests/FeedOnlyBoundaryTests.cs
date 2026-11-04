using System.Text.Json;
using System.Xml.Linq;
using Xunit;

namespace Package.Sample.Tests;

/// <summary>
/// The sample proves the packages only if it consumes them the way a fork does: from the feed, never from source.
/// These checks fail when a Codelab ProjectReference, a source-mode import or another Codelab package source sneaks in.
/// </summary>
public sealed class FeedOnlyBoundaryTests
{
    private const string CodelabPrefix = "Codelab.";
    private const string SamplesMarker = "Package.Sample.Api/Package.Sample.Api.csproj";
    private const string FeedSourceKey = "codelab-local";

    [Fact]
    public void No_sample_project_references_a_codelab_project()
    {
        var projects = Directory.GetFiles(SamplesFolder(), "*.csproj", SearchOption.AllDirectories)
            .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
            .ToList();

        Assert.NotEmpty(projects);
        foreach (var project in projects)
        {
            var references = XDocument.Load(project).Descendants("ProjectReference").Select(reference => (string?)reference.Attribute("Include") ?? string.Empty);
            Assert.DoesNotContain(references, include => include.Contains(CodelabPrefix, StringComparison.OrdinalIgnoreCase)
                || include.Contains("packages\\dotnet", StringComparison.OrdinalIgnoreCase)
                || include.Contains("packages/dotnet", StringComparison.OrdinalIgnoreCase));
        }
    }

    [Fact]
    public void The_build_boundary_uses_the_feed_sdk_and_never_imports_the_repository_build_files()
    {
        var props = XDocument.Load(Path.Combine(SamplesFolder(), "Directory.Build.props"));

        Assert.Equal("false", props.Descendants("UseCodelabPackageSource").Single().Value);
        Assert.Contains(props.Descendants("Import"), import => (string?)import.Attribute("Sdk") == "Codelab.Build.Sdk");
        Assert.DoesNotContain(props.Descendants("Import"), import => ((string?)import.Attribute("Project") ?? string.Empty).Contains("..", StringComparison.Ordinal));
        Assert.True(File.Exists(Path.Combine(SamplesFolder(), "Directory.Packages.props")));
        Assert.True(File.Exists(Path.Combine(SamplesFolder(), "Directory.Build.targets")));
    }

    [Fact]
    public void Codelab_packages_resolve_only_from_the_feed_source()
    {
        var config = XDocument.Load(Path.Combine(SamplesFolder(), "nuget.config"));
        var sources = config.Descendants("packageSources").Single();

        Assert.NotNull(sources.Element("clear"));
        var codelabMappings = config.Descendants("packageSource")
            .Where(source => source.Elements("package").Any(package => ((string?)package.Attribute("pattern") ?? string.Empty).StartsWith(CodelabPrefix, StringComparison.Ordinal)))
            .Select(source => (string?)source.Attribute("key"))
            .ToList();
        Assert.Equal([FeedSourceKey], codelabMappings);
        var feed = sources.Elements("add").Single(source => (string?)source.Attribute("key") == FeedSourceKey);
        Assert.EndsWith("feed/nuget", (string?)feed.Attribute("value"), StringComparison.Ordinal);
    }

    [Fact]
    public void Every_codelab_assembly_in_the_test_run_came_from_a_package()
    {
        var depsFile = Directory.GetFiles(AppContext.BaseDirectory, "Package.Sample.Tests.deps.json").Single();
        using var deps = JsonDocument.Parse(File.ReadAllText(depsFile));

        var codelab = deps.RootElement.GetProperty("libraries").EnumerateObject()
            .Where(library => library.Name.StartsWith(CodelabPrefix, StringComparison.Ordinal))
            .ToList();

        Assert.Equal(5, codelab.Count);
        Assert.All(codelab, library => Assert.Equal("package", library.Value.GetProperty("type").GetString()));
    }

    private static string SamplesFolder()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, SamplesMarker)))
            {
                return directory.FullName;
            }
        }

        throw new DirectoryNotFoundException($"No folder holding {SamplesMarker} above {AppContext.BaseDirectory}.");
    }
}
