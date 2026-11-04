using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Codelab.Build.Analyzers.Options;

/// <summary>
/// A compilation is a test project when its MSBuild project name (made visible by Codelab.Build.Sdk) ends with ".Tests"
/// or ".Test", the same path rule base.no-test-project-opt-in uses. Without that wiring the assembly name is used.
/// IsTestProject is NOT trusted: any project turns it on by referencing Microsoft.NET.Test.Sdk, and an AssemblyName
/// override would otherwise rename production code into a test project, switching off CLB1004.
/// </summary>
internal static class TestProjectDetector
{
    private static readonly string[] TestNameSuffixes = [".Tests", ".Test"];

    public static bool IsTestProject(Compilation compilation, AnalyzerConfigOptions globalOptions)
    {
        var projectName = globalOptions.TryGetValue(OptionKeys.ProjectNameProperty, out var value) && !string.IsNullOrWhiteSpace(value)
            ? value.Trim()
            : compilation.AssemblyName;
        return HasTestName(projectName);
    }

    private static bool HasTestName(string? name) =>
        name is not null
        && TestNameSuffixes.Any(suffix => name.EndsWith(suffix, StringComparison.OrdinalIgnoreCase));
}
