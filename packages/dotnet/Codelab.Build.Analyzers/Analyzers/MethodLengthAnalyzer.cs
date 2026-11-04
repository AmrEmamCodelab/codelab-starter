using System.Collections.Immutable;
using Codelab.Build.Analyzers.Measurement;
using Codelab.Build.Analyzers.Options;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Codelab.Build.Analyzers.Analyzers;

/// <summary>CLB1001: a member body (method, constructor, operator, accessor, expression-bodied property) is too long.</summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class MethodLengthAnalyzer : DiagnosticAnalyzer
{
    private static readonly SyntaxKind[] MemberKinds =
    [
        SyntaxKind.MethodDeclaration,
        SyntaxKind.ConstructorDeclaration,
        SyntaxKind.DestructorDeclaration,
        SyntaxKind.OperatorDeclaration,
        SyntaxKind.ConversionOperatorDeclaration,
        SyntaxKind.LocalFunctionStatement,
        SyntaxKind.GetAccessorDeclaration,
        SyntaxKind.SetAccessorDeclaration,
        SyntaxKind.InitAccessorDeclaration,
        SyntaxKind.AddAccessorDeclaration,
        SyntaxKind.RemoveAccessorDeclaration,
        SyntaxKind.PropertyDeclaration,
        SyntaxKind.IndexerDeclaration,
    ];

    /// <inheritdoc />
    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics { get; } =
        ImmutableArray.Create(Descriptors.MethodTooLong);

    /// <inheritdoc />
    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        context.RegisterCompilationStartAction(start =>
        {
            var thresholds = new ThresholdCache(start.Options.AnalyzerConfigOptionsProvider);
            start.RegisterSyntaxNodeAction(node => Analyze(node, thresholds), MemberKinds);
        });
    }

    private static void Analyze(SyntaxNodeAnalysisContext context, ThresholdCache thresholds)
    {
        if (MemberBodies.Describe(context.Node) is not { } member)
        {
            return;
        }

        var limit = thresholds.For(context.Node.SyntaxTree).Sizes.MethodLines;
        var lines = CodeLineCounter.Count(member.Body);
        if (lines > limit)
        {
            context.ReportDiagnostic(Diagnostic.Create(Descriptors.MethodTooLong, member.Location, member.Name, lines, limit));
        }
    }
}
