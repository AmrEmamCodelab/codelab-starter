using System.Globalization;
using Codelab.Build.Analyzers.Analyzers;
using Codelab.Build.Analyzers.Options;
using Codelab.Build.Analyzers.Tests.Harness;

namespace Codelab.Build.Analyzers.Tests;

public sealed class ParameterCountAnalyzerTests
{
    private static readonly string Five = SourceText.Parameters(DefaultThresholds.Parameters);
    private static readonly string Six = SourceText.Parameters(DefaultThresholds.Parameters + 1);

    private static AnalyzerTest Test() => new(new ParameterCountAnalyzer());

    [Fact]
    public async Task Five_parameters_pass_and_six_are_CLB1003()
    {
        var atLimit = await Test().RunAsync($"public class Sample {{ public void Run({Five}) {{ }} }}");
        var overLimit = await Test().RunAsync($"public class Sample {{ public void Run({Six}) {{ }} }}");

        Assert.Empty(atLimit);
        var diagnostic = Assert.Single(overLimit);
        Assert.Equal(DiagnosticIds.TooManyParameters, diagnostic.Id);
        Assert.StartsWith("'Run' has 6 parameters; the limit is 5 ", diagnostic.GetMessage(CultureInfo.InvariantCulture), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("public class Sample {{ public Sample({0}) {{ }} }}", "Sample")]
    [InlineData("public class Sample({0}) {{ }}", "Sample")]
    [InlineData("public struct Sample({0}) {{ }}", "Sample")]
    [InlineData("public delegate void Handler({0});", "Handler")]
    [InlineData("public class Sample {{ public void Run() {{ void Local({0}) {{ }} Local(0, 0, 0, 0, 0, 0); }} }}", "Local")]
    [InlineData("public class Sample {{ public int this[{0}] => 0; }}", "this")]
    public async Task Constructors_primary_constructors_delegates_local_functions_and_indexers_are_checked(string template, string name)
    {
        var diagnostics = await Test().RunAsync(string.Format(CultureInfo.InvariantCulture, template, Six));

        Assert.StartsWith($"'{name}' has 6 parameters", Assert.Single(diagnostics).GetMessage(CultureInfo.InvariantCulture), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("public abstract class Base {{ public abstract void Run({0}); }}\npublic class Sample : Base {{ public override void Run({0}) {{ }} }}")]
    [InlineData("public interface IRun {{ void Run({0}); }}\npublic class Sample : IRun {{ void IRun.Run({0}) {{ }} }}")]
    public async Task Only_the_declaring_signature_is_reported_not_its_override_or_explicit_implementation(string template)
    {
        var diagnostics = await Test().RunAsync(string.Format(CultureInfo.InvariantCulture, template, Six));

        var declaration = Assert.Single(diagnostics);
        Assert.Equal(0, declaration.Location.GetLineSpan().StartLinePosition.Line);
    }

    [Fact]
    public async Task Record_primary_constructors_are_data_shapes_and_are_not_reported()
    {
        var diagnostics = await Test().RunAsync($"public record Sample({Six});");

        Assert.Empty(diagnostics);
    }

    [Fact]
    public async Task Extern_methods_are_not_reported()
    {
        var source = $"public static class Native {{ [System.Runtime.InteropServices.DllImport(\"native\")] public static extern void Call({Six}); }}";

        var diagnostics = await Test().RunAsync(source);

        Assert.Empty(diagnostics);
    }

    [Fact]
    public async Task The_limit_comes_from_codelab_parameters()
    {
        var diagnostics = await Test()
            .WithOption(OptionKeys.Parameters, "2")
            .RunAsync($"public class Sample {{ public void Run({SourceText.Parameters(count: 3)}) {{ }} }}");

        Assert.StartsWith("'Run' has 3 parameters; the limit is 2 ", Assert.Single(diagnostics).GetMessage(CultureInfo.InvariantCulture), StringComparison.Ordinal);
    }
}
