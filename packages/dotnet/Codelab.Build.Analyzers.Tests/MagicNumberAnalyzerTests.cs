using System.Globalization;
using Codelab.Build.Analyzers.Analyzers;
using Codelab.Build.Analyzers.Options;
using Codelab.Build.Analyzers.Tests.Harness;

namespace Codelab.Build.Analyzers.Tests;

public sealed class MagicNumberAnalyzerTests
{
    private static AnalyzerTest Test() => new(new MagicNumberAnalyzer());

    private static string InMethod(string statement) =>
        $"public class Sample {{ public double Run(int input) {{ {statement} }} }}";

    [Theory]
    [InlineData("return 42;", "42")]
    [InlineData("return -2;", "-2")]
    [InlineData("return 2.5;", "2.5")]
    [InlineData("return 0x10;", "0x10")]
    [InlineData("return input * 1000L;", "1000L")]
    public async Task A_bare_number_in_code_is_CLB1004(string statement, string literal)
    {
        var diagnostics = await Test().RunAsync(InMethod(statement));

        var diagnostic = Assert.Single(diagnostics);
        Assert.Equal(DiagnosticIds.MagicNumber, diagnostic.Id);
        Assert.Equal($"Magic number {literal}: give it a name (const, static readonly field, options property or enum member) (rule base.no-magic-values). Allowed bare numbers: -1, 0, 1.", diagnostic.GetMessage(CultureInfo.InvariantCulture));
    }

    [Theory]
    [InlineData("return 0;")]
    [InlineData("return 1;")]
    [InlineData("return -1;")]
    [InlineData("return 1.0;")]
    [InlineData("return input + 0u;")]
    public async Task The_allowed_numbers_pass(string statement)
    {
        var diagnostics = await Test().RunAsync(InMethod(statement));

        Assert.Empty(diagnostics);
    }

    [Theory]
    [InlineData("public class Sample { private const int Limit = 42; }")]
    [InlineData("public class Sample { public int Run() { const int Limit = 42; return Limit; } }")]
    [InlineData("public class Sample { private static readonly System.TimeSpan Timeout = System.TimeSpan.FromSeconds(30); }")]
    [InlineData("public class Sample { private readonly int _limit = 42; }")]
    [InlineData("public class Sample { public int Limit { get; set; } = 42; }")]
    [InlineData("public enum Status { Draft = 1, Submitted = 2, Approved = 3 }")]
    [InlineData("[System.Obsolete(\"x\", false, DiagnosticId = \"A\" )] public class Sample { [System.ComponentModel.DefaultValue(42)] public int Value { get; set; } }")]
    [InlineData("public class Sample { public int Run(int retries = 3) => retries; }")]
    public async Task Numbers_the_code_already_names_pass(string source)
    {
        var diagnostics = await Test().RunAsync(source);

        Assert.Empty(diagnostics);
    }

    [Fact]
    public async Task A_mutable_field_initializer_is_not_a_name()
    {
        var diagnostics = await Test().RunAsync("public class Sample { private int _counter = 42; public int Next() => ++_counter; }");

        Assert.Single(diagnostics);
    }

    [Fact]
    public async Task Allowed_numbers_come_from_codelab_allowed_numbers()
    {
        var diagnostics = await Test()
            .WithOption(OptionKeys.AllowedNumbers, "0, 1, 100")
            .RunAsync(InMethod("return input > 100 ? 100 : -1;"));

        var diagnostic = Assert.Single(diagnostics);
        Assert.EndsWith("Allowed bare numbers: 0, 1, 100.", diagnostic.GetMessage(CultureInfo.InvariantCulture), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("Starter.Sample.Tests")]
    [InlineData("Starter.Sample.Test")]
    public async Task Test_assemblies_are_skipped_by_name_when_exclude_tests_is_on(string assemblyName)
    {
        var diagnostics = await Test().WithAssemblyName(assemblyName).RunAsync(InMethod("return 42;"));

        Assert.Empty(diagnostics);
    }

    [Fact]
    public async Task Test_projects_are_skipped_through_the_project_name_build_property()
    {
        var diagnostics = await Test()
            .WithGlobalOption(OptionKeys.ProjectNameProperty, "Starter.Sample.Tests")
            .RunAsync(InMethod("return 42;"));

        Assert.Empty(diagnostics);
    }

    /// <summary>
    /// Regression: referencing Microsoft.NET.Test.Sdk sets IsTestProject=true, and an AssemblyName override can end in
    /// .Tests. Neither makes a production project (its project name) a test project.
    /// </summary>
    [Theory]
    [InlineData("build_property.IsTestProject", "true", "Starter.Sample")]
    [InlineData("build_property.IsTestProject", "true", "Starter.Sample.Tests")]
    public async Task A_production_project_that_claims_to_be_a_test_project_is_still_checked(string property, string value, string assemblyName)
    {
        var diagnostics = await Test()
            .WithGlobalOption(OptionKeys.ProjectNameProperty, "Starter.Sample")
            .WithGlobalOption(property, value)
            .WithAssemblyName(assemblyName)
            .RunAsync(InMethod("return 42;"));

        Assert.Single(diagnostics);
    }

    [Fact]
    public async Task Test_projects_are_checked_when_exclude_tests_is_off()
    {
        var diagnostics = await Test()
            .WithAssemblyName("Starter.Sample.Tests")
            .WithOption(OptionKeys.ExcludeTests, "false")
            .RunAsync(InMethod("return 42;"));

        Assert.Single(diagnostics);
    }

    [Fact]
    public async Task A_module_exempt_from_the_rule_is_not_checked()
    {
        var diagnostics = await Test()
            .WithOption(OptionKeys.MagicNumbersEnforced, "false")
            .RunAsync(InMethod("return 42;"));

        Assert.Empty(diagnostics);
    }
}
