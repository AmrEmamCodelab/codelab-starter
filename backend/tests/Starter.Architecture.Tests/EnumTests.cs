using Codelab.Testing.Checks;
using Starter.Architecture.Tests.Support;

namespace Starter.Architecture.Tests;

/// <summary>Rule <c>base.enums-as-int</c>: in source (explicit values) and in the compiled assemblies (underlying int).</summary>
public sealed class EnumTests
{
    private const string BackendSourceFolder = "backend/src";
    private const string EnumKeyword = "enum"; // samples are built at run time so the repository content rule does not flag them

    [Fact]
    public void EveryBackendEnumIsIntWithExplicitValues()
    {
        var findings = EnumDeclarationScanner.ScanDirectory(Path.Combine(StarterSolution.RepositoryRootPath, BackendSourceFolder));
        var violations = findings.SelectMany(finding => finding.Problems.Select(problem => $"{finding.Location} enum {finding.Name}: {problem}"));

        new CheckResult("Backend enums are int with explicit values", violations, findings.Count).ThrowIfFailed();
    }

    [Fact]
    public void EveryCompiledEnumHasAnIntUnderlyingType()
    {
        var enums = StarterSolution.All.SelectMany(assembly => assembly.GetTypes()).Where(type => type.IsEnum).ToList();
        var violations = enums
            .Where(type => Enum.GetUnderlyingType(type) != typeof(int))
            .Select(type => $"{type.FullName} is stored as {Enum.GetUnderlyingType(type).Name}");

        new CheckResult("Compiled enums are int", violations, enums.Count).ThrowIfFailed();
    }

    [Fact]
    public void AnEnumWithExplicitValuesPasses()
    {
        var findings = EnumDeclarationScanner.Scan("public enum Phase { First = 1, Second = 2 }", "good.cs");

        var finding = Assert.Single(findings);
        Assert.Empty(finding.Problems);
    }

    [Fact]
    public void AnExplicitIntUnderlyingTypePasses()
    {
        var finding = Assert.Single(EnumDeclarationScanner.Scan("enum Phase : int { First = 1 }", "int.cs"));

        Assert.Empty(finding.Problems);
    }

    [Fact]
    public void AMemberWithoutAValueIsReported()
    {
        var finding = Assert.Single(EnumDeclarationScanner.Scan($"{EnumKeyword} Phase {{ First = 1, Second }}", "implicit.cs"));

        Assert.Equal("implicit.cs:1", finding.Location);
        Assert.Contains(finding.Problems, problem => problem.Contains("'Second'", StringComparison.Ordinal));
    }

    [Fact]
    public void ANonIntUnderlyingTypeIsReported()
    {
        var finding = Assert.Single(EnumDeclarationScanner.Scan("enum Size : byte { Small = 1 }", "byte.cs"));

        Assert.Contains(finding.Problems, problem => problem.Contains("byte", StringComparison.Ordinal));
    }

    [Fact]
    public void NestedEnumsAreFound()
    {
        var findings = EnumDeclarationScanner.Scan("class Outer { enum Inner { A = 1 } }", "nested.cs");

        Assert.Equal("Inner", Assert.Single(findings).Name);
    }
}
