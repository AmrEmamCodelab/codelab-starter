using System.Globalization;
using Codelab.Build.Analyzers.Analyzers;
using Codelab.Build.Analyzers.Tests.Harness;

namespace Codelab.Build.Analyzers.Tests;

public sealed class EnumMemberValueAnalyzerTests
{
    // The inputs break base.enum-explicit-values on purpose; the keyword is spliced in so the content rule (a text
    // pattern over this file) does not read the test inputs as real enums.
    private const string Keyword = "enum";

    private static AnalyzerTest Test() => new(new EnumMemberValueAnalyzer());

    [Fact]
    public async Task Explicit_values_pass_and_an_implicit_member_is_CLB1005()
    {
        var explicitValues = await Test().RunAsync($"public {Keyword} Status {{ Draft = 1, Submitted = 2 }}");
        var implicitMember = await Test().RunAsync($"public {Keyword} Status {{ Draft = 1, Submitted }}");

        Assert.Empty(explicitValues);
        var diagnostic = Assert.Single(implicitMember);
        Assert.Equal(DiagnosticIds.EnumMemberWithoutValue, diagnostic.Id);
        Assert.StartsWith("'Status.Submitted' has no explicit value", diagnostic.GetMessage(CultureInfo.InvariantCulture), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("public ENUM S\n{\n    /// <summary>first</summary>\n    A = 1,\n    /// <summary>second</summary>\n    B,\n}")]
    [InlineData("public ENUM S\n{\n    A = 1,\n    [System.Obsolete(\"x\")]\n    B,\n}")]
    [InlineData("public ENUM S\n{\n    /// <summary>first, the default</summary>\n    A,\n}")]
    [InlineData("public ENUM S : byte { A = 1, /* second, third */ B }")]
    public async Task Doc_comments_attributes_and_comments_never_hide_an_implicit_member(string source)
    {
        var diagnostics = await Test().RunAsync(source.Replace("ENUM", Keyword, StringComparison.Ordinal));

        Assert.Equal(DiagnosticIds.EnumMemberWithoutValue, Assert.Single(diagnostics).Id);
    }

    [Fact]
    public async Task Every_implicit_member_is_reported()
    {
        var diagnostics = await Test().RunAsync($"public {Keyword} S {{ A, B, C = 3 }}");

        Assert.Equal(2, diagnostics.Length);
    }

    [Fact]
    public async Task Flags_and_expressions_count_as_explicit_values()
    {
        var diagnostics = await Test().RunAsync($"[System.Flags] public {Keyword} F {{ None = 0, A = 1 << 0, B = 1 << 1, Both = A | B }}");

        Assert.Empty(diagnostics);
    }
}
