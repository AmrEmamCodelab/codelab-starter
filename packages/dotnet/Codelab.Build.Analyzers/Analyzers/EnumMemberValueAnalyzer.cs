using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Codelab.Build.Analyzers.Analyzers;

/// <summary>
/// CLB1005: an enum member without an explicit value (rule base.enums-as-int). Enums are stored and sent as integers,
/// so an implicit value changes meaning when members are reordered or inserted. Matched on syntax, so doc comments and
/// attributes on a member never hide it (the text pattern of base.enum-explicit-values could not see past them).
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class EnumMemberValueAnalyzer : DiagnosticAnalyzer
{
    /// <inheritdoc />
    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics { get; } =
        ImmutableArray.Create(Descriptors.EnumMemberWithoutValue);

    /// <inheritdoc />
    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        context.RegisterSyntaxNodeAction(Analyze, SyntaxKind.EnumMemberDeclaration);
    }

    private static void Analyze(SyntaxNodeAnalysisContext context)
    {
        var member = (EnumMemberDeclarationSyntax)context.Node;
        if (member.EqualsValue is null && member.Parent is EnumDeclarationSyntax declaration)
        {
            context.ReportDiagnostic(Diagnostic.Create(
                Descriptors.EnumMemberWithoutValue, member.Identifier.GetLocation(), declaration.Identifier.ValueText, member.Identifier.ValueText));
        }
    }
}
