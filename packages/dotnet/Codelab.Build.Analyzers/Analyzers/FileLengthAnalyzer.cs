using System.Collections.Immutable;
using Codelab.Build.Analyzers.Measurement;
using Codelab.Build.Analyzers.Options;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Text;

namespace Codelab.Build.Analyzers.Analyzers;

/// <summary>CLB1002: a source file has more code lines than codelab_file_lines. Reported on the first line.</summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class FileLengthAnalyzer : DiagnosticAnalyzer
{
    /// <inheritdoc />
    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics { get; } =
        ImmutableArray.Create(Descriptors.FileTooLong);

    /// <inheritdoc />
    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        context.RegisterCompilationStartAction(start =>
        {
            var thresholds = new ThresholdCache(start.Options.AnalyzerConfigOptionsProvider);
            start.RegisterSyntaxTreeAction(tree => Analyze(tree, thresholds));
        });
    }

    private static void Analyze(SyntaxTreeAnalysisContext context, ThresholdCache thresholds)
    {
        var limit = thresholds.For(context.Tree).Sizes.FileLines;
        var lines = CodeLineCounter.Count(context.Tree.GetRoot(context.CancellationToken));
        if (lines <= limit)
        {
            return;
        }

        var location = Location.Create(context.Tree, new TextSpan(start: 0, length: 0));
        var fileName = Path.GetFileName(context.Tree.FilePath);
        context.ReportDiagnostic(Diagnostic.Create(Descriptors.FileTooLong, location, fileName, lines, limit));
    }
}
