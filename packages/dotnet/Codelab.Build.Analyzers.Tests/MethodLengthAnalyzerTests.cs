using System.Globalization;
using Codelab.Build.Analyzers.Analyzers;
using Codelab.Build.Analyzers.Options;
using Codelab.Build.Analyzers.Tests.Harness;

namespace Codelab.Build.Analyzers.Tests;

public sealed class MethodLengthAnalyzerTests
{
    private static AnalyzerTest Test() => new(new MethodLengthAnalyzer());

    [Fact]
    public async Task A_body_at_the_default_limit_passes_and_one_line_more_is_CLB1001()
    {
        var atLimit = await Test().RunAsync(SourceText.ClassWithMethod("Run", DefaultThresholds.MethodLines));
        var overLimit = await Test().RunAsync(SourceText.ClassWithMethod("Run", DefaultThresholds.MethodLines + 1));

        Assert.Empty(atLimit);
        var diagnostic = Assert.Single(overLimit);
        Assert.Equal(DiagnosticIds.MethodTooLong, diagnostic.Id);
        Assert.Equal("'Run' has 41 lines of code; the limit is 40 (codelab_method_lines, rule base.clean-code). Extract smaller single-purpose methods.", diagnostic.GetMessage(CultureInfo.InvariantCulture));
    }

    [Fact]
    public async Task Blank_and_comment_lines_do_not_count()
    {
        // ClassWithMethod adds a blank and a comment line after every statement: 3 * 38 physical lines, 40 code lines.
        var diagnostics = await Test().RunAsync(SourceText.ClassWithMethod("Run", DefaultThresholds.MethodLines));

        Assert.Empty(diagnostics);
    }

    [Fact]
    public async Task The_tree_option_from_the_module_editorconfig_wins_over_the_global_option()
    {
        var diagnostics = await Test()
            .WithGlobalOption(OptionKeys.MethodLines, "100")
            .WithOption(OptionKeys.MethodLines, "10")
            .RunAsync(SourceText.ClassWithMethod("Run", bodyLines: 11));

        Assert.StartsWith("'Run' has 11 lines of code; the limit is 10 ", Assert.Single(diagnostics).GetMessage(CultureInfo.InvariantCulture), StringComparison.Ordinal);
    }

    [Fact]
    public async Task The_global_option_applies_when_the_tree_has_none()
    {
        var diagnostics = await Test()
            .WithGlobalOption(OptionKeys.MethodLines, "10")
            .RunAsync(SourceText.ClassWithMethod("Run", bodyLines: 11));

        Assert.Single(diagnostics);
    }

    [Theory]
    [InlineData("public Sample() { _ = 0;\n_ = 0; }", "Sample constructor")]
    [InlineData("public int Value { get { return 0;\n} }", "Value.get")]
    [InlineData("public int Value =>\n0;", "Value")]
    [InlineData("public void Outer() { void Inner() { _ = 0;\n_ = 0; } Inner(); }", "Inner")]
    [InlineData("public static Sample operator +(Sample a, Sample b) { _ = 0;\nreturn a; }", "operator +")]
    public async Task Constructors_accessors_expression_bodies_local_functions_and_operators_are_measured(string member, string name)
    {
        var diagnostics = await Test()
            .WithOption(OptionKeys.MethodLines, "1")
            .RunAsync($"public class Sample {{ {member} }}");

        Assert.Contains(diagnostics, diagnostic => diagnostic.GetMessage(CultureInfo.InvariantCulture).StartsWith($"'{name}' has 2 lines", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Members_without_a_body_are_not_measured()
    {
        const string Source = "public abstract class Sample { public abstract void Run(); public int Value { get; set; } }";

        var diagnostics = await Test().WithOption(OptionKeys.MethodLines, "1").RunAsync(Source);

        Assert.Empty(diagnostics);
    }

    [Fact]
    public async Task An_invalid_option_value_falls_back_to_the_default_instead_of_disabling_the_gate()
    {
        var diagnostics = await Test()
            .WithOption(OptionKeys.MethodLines, "forty")
            .RunAsync(SourceText.ClassWithMethod("Run", DefaultThresholds.MethodLines + 1));

        Assert.Single(diagnostics);
    }
}
