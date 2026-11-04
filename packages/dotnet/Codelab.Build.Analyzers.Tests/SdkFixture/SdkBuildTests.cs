using System.Diagnostics;
using System.Reflection;
using System.Runtime.Loader;
using System.Security.Cryptography;

namespace Codelab.Build.Analyzers.Tests.SdkFixture;

/// <summary>
/// ADR-012 phase-1 exit criterion and the SDK's own gates, proven with real builds of Fixtures/SdkConsumer:
/// a method over methodLines, a file over fileLines, too many parameters and a magic number each fail
/// <c>dotnet build -warnaserror</c>; Thread.Sleep is a global banned API; release stamping reaches the assembly.
/// </summary>
public sealed class SdkBuildTests(FixtureWorkspace workspace) : IClassFixture<FixtureWorkspace>
{
    private const string Release = "1.4.0-rc.2";
    private const int ShortCommitLength = 7;
    private const int CommitBytes = 20;
    private const string FixtureGateOptOut = "-p:CodelabFixtureGateOptOut=true";
    private static readonly string[] BannedApiError = ["RS0030"];

    /// <summary>A fresh 40-hex-digit sha per run, so no fixed value can collide with a known digest.</summary>
    private static readonly string Commit = Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(CommitBytes));

    public static TheoryData<string, string, string> Violations => new()
    {
        { "LongMethod.cs", ViolationSources.MethodOverLimit(), "CLB1001" },
        { "Constants.cs", ViolationSources.FileOverLimit(), "CLB1002" },
        { "Sums.cs", ViolationSources.FromFixture("Sums.cs"), "CLB1003" },
        { "Durations.cs", ViolationSources.FromFixture("Durations.cs"), "CLB1004" },
    };

    [Fact]
    public async Task The_clean_fixture_builds_with_warnings_as_errors()
    {
        var result = await workspace.BuildAsync();

        Assert.True(result.ExitCode == 0, result.Output);
    }

    [Theory]
    [MemberData(nameof(Violations))]
    public async Task Each_clean_code_violation_fails_the_build_with_its_own_id(string fileName, string source, string expectedId)
    {
        using var violation = workspace.AddSource(fileName, source);

        var result = await workspace.BuildAsync();

        Assert.NotEqual(0, result.ExitCode);
        Assert.Equal(new[] { expectedId }, result.ErrorIds.ToArray());
        Assert.Contains($"{fileName}(", result.Output, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Thread_sleep_is_banned_globally_with_the_fix_message()
    {
        using var violation = workspace.AddSource("Waits.cs", ViolationSources.FromFixture("Waits.cs"));

        var result = await workspace.BuildAsync();

        Assert.NotEqual(0, result.ExitCode);
        Assert.Equal(BannedApiError, result.ErrorIds.ToArray());
        Assert.Contains("Sleep(int)", result.Output, StringComparison.Ordinal);
        Assert.Contains("Sleep(TimeSpan)", result.Output, StringComparison.Ordinal);
        Assert.Contains("Use await Task.Delay(delay, timeProvider, cancellationToken)", result.Output, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Release_and_commit_are_stamped_on_the_assembly()
    {
        var environment = new Dictionary<string, string> { ["CODELAB_RELEASE"] = Release, ["CODELAB_COMMIT"] = Commit };

        // Analyzers off here: the release would otherwise also restamp and rebuild the analyzer project in the repo.
        var result = await workspace.BuildAsync(environment, "-p:CodelabEnableBuildAnalyzers=false", FixtureGateOptOut);

        Assert.True(result.ExitCode == 0, result.Output);
        var version = FileVersionInfo.GetVersionInfo(workspace.OutputAssemblyPath);
        Assert.Equal($"{Release}+{Commit[..ShortCommitLength]}", version.ProductVersion);
        Assert.Equal((1, 4, 0, 0), (version.FileMajorPart, version.FileMinorPart, version.FileBuildPart, version.FilePrivatePart));
        Assert.Equal(Release, ReadAssemblyMetadata(workspace.OutputAssemblyPath, key: "CodelabRelease"));
    }

    [Fact]
    public async Task A_release_that_is_not_semver_fails_with_CODELAB001()
    {
        var environment = new Dictionary<string, string> { ["CODELAB_RELEASE"] = "release-7" };

        var result = await workspace.BuildAsync(environment, "-p:CodelabEnableBuildAnalyzers=false", FixtureGateOptOut);

        Assert.NotEqual(0, result.ExitCode);
        Assert.Contains("CODELAB001", result.ErrorIds);
    }

    /// <summary>Regression: any value other than 'true', or the .NET analyzers turned off, fails the build itself.</summary>
    [Theory]
    [InlineData("-p:CodelabEnableBuildAnalyzers=False", "CODELAB002")]
    [InlineData("-p:CodelabEnableBannedApis=off", "CODELAB002")]
    [InlineData("-p:EnableNETAnalyzers=false", "CODELAB003")]
    [InlineData("-p:AnalysisLevel=none", "CODELAB003")]
    [InlineData("-p:AnalysisMode=None", "CODELAB003")]
    public async Task Switching_a_quality_gate_off_fails_the_build(string property, string expectedId)
    {
        var result = await workspace.BuildAsync(property);

        Assert.NotEqual(0, result.ExitCode);
        Assert.Contains(expectedId, result.ErrorIds);
    }

    private static string? ReadAssemblyMetadata(string assemblyPath, string key)
    {
        var context = new AssemblyLoadContext(nameof(ReadAssemblyMetadata), isCollectible: true);
        try
        {
            using var stream = new MemoryStream(File.ReadAllBytes(assemblyPath));
            return context.LoadFromStream(stream)
                .GetCustomAttributes<AssemblyMetadataAttribute>()
                .SingleOrDefault(attribute => attribute.Key == key)?.Value;
        }
        finally
        {
            context.Unload();
        }
    }
}
