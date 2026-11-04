using System.Collections.Immutable;
using System.Globalization;
using Codelab.Build.Analyzers.MagicNumbers;
using Codelab.Build.Analyzers.Options;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Codelab.Build.Analyzers.Analyzers;

/// <summary>
/// CLB1004: a numeric literal that is neither an allowed number (codelab_allowed_numbers, default -1, 0, 1) nor in a
/// naming context (<see cref="NamingContext"/>). Test projects are skipped when codelab_exclude_tests is true, and a
/// module that exempts base.no-magic-values gets codelab_magic_numbers_enforced = false.
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class MagicNumberAnalyzer : DiagnosticAnalyzer
{
    private const string AllowedNumberSeparator = ", ";

    /// <inheritdoc />
    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics { get; } =
        ImmutableArray.Create(Descriptors.MagicNumber);

    /// <inheritdoc />
    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        context.RegisterCompilationStartAction(start =>
        {
            var provider = start.Options.AnalyzerConfigOptionsProvider;
            var isTestProject = TestProjectDetector.IsTestProject(start.Compilation, provider.GlobalOptions);
            var thresholds = new ThresholdCache(provider);
            start.RegisterSyntaxNodeAction(node => Analyze(node, thresholds, isTestProject), SyntaxKind.NumericLiteralExpression);
        });
    }

    private static void Analyze(SyntaxNodeAnalysisContext context, ThresholdCache thresholds, bool isTestProject)
    {
        var policy = thresholds.For(context.Node.SyntaxTree).MagicNumbers;
        if (!policy.Enforced || (isTestProject && policy.ExcludeTests))
        {
            return;
        }

        if (NumericLiteral.From((LiteralExpressionSyntax)context.Node) is not { } literal
            || policy.IsAllowed(literal.Value)
            || NamingContext.IsNamed(literal.Expression))
        {
            return;
        }

        var allowed = string.Join(AllowedNumberSeparator, policy.AllowedNumbers.Select(number => number.ToString(CultureInfo.InvariantCulture)));
        context.ReportDiagnostic(Diagnostic.Create(Descriptors.MagicNumber, literal.Expression.GetLocation(), literal.Text, allowed));
    }
}
