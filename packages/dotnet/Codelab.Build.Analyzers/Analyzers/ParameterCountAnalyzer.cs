using System.Collections.Immutable;
using Codelab.Build.Analyzers.Options;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Codelab.Build.Analyzers.Analyzers;

/// <summary>
/// CLB1003: a method, constructor, local function, delegate, indexer or class/struct primary constructor declares more
/// parameters than codelab_parameters. Signatures the author does not own are skipped: overrides, explicit interface
/// implementations and extern methods. Record primary constructors are data shapes, not calls, and are skipped too.
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class ParameterCountAnalyzer : DiagnosticAnalyzer
{
    private static readonly SyntaxKind[] DeclarationKinds =
    [
        SyntaxKind.MethodDeclaration,
        SyntaxKind.ConstructorDeclaration,
        SyntaxKind.LocalFunctionStatement,
        SyntaxKind.DelegateDeclaration,
        SyntaxKind.IndexerDeclaration,
        SyntaxKind.ClassDeclaration,
        SyntaxKind.StructDeclaration,
    ];

    /// <inheritdoc />
    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics { get; } =
        ImmutableArray.Create(Descriptors.TooManyParameters);

    /// <inheritdoc />
    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        context.RegisterCompilationStartAction(start =>
        {
            var thresholds = new ThresholdCache(start.Options.AnalyzerConfigOptionsProvider);
            start.RegisterSyntaxNodeAction(node => Analyze(node, thresholds), DeclarationKinds);
        });
    }

    private static void Analyze(SyntaxNodeAnalysisContext context, ThresholdCache thresholds)
    {
        if (OwnedSignature.Of(context.Node) is not { } signature)
        {
            return;
        }

        var limit = thresholds.For(context.Node.SyntaxTree).Sizes.Parameters;
        var count = signature.Parameters.Parameters.Count;
        if (count > limit)
        {
            var name = signature.NameToken;
            context.ReportDiagnostic(Diagnostic.Create(Descriptors.TooManyParameters, name.GetLocation(), name.ValueText, count, limit));
        }
    }
}
