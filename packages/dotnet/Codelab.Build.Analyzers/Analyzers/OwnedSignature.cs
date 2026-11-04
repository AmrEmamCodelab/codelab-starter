using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Codelab.Build.Analyzers.Analyzers;

/// <summary>
/// The parameter list CLB1003 measures and the token it reports on. Null for signatures the author does not own
/// (override, explicit interface implementation, extern) and for record primary constructors.
/// </summary>
internal sealed class OwnedSignature
{
    private OwnedSignature(SyntaxToken nameToken, BaseParameterListSyntax parameters)
    {
        NameToken = nameToken;
        Parameters = parameters;
    }

    public SyntaxToken NameToken { get; }

    public BaseParameterListSyntax Parameters { get; }

    public static OwnedSignature? Of(SyntaxNode node) => node switch
    {
        MethodDeclarationSyntax method when IsOwned(method.ExplicitInterfaceSpecifier, method.Modifiers) => new OwnedSignature(method.Identifier, method.ParameterList),
        ConstructorDeclarationSyntax ctor => new OwnedSignature(ctor.Identifier, ctor.ParameterList),
        LocalFunctionStatementSyntax local => new OwnedSignature(local.Identifier, local.ParameterList),
        DelegateDeclarationSyntax del => new OwnedSignature(del.Identifier, del.ParameterList),
        IndexerDeclarationSyntax indexer when IsOwned(indexer.ExplicitInterfaceSpecifier, indexer.Modifiers) => new OwnedSignature(indexer.ThisKeyword, indexer.ParameterList),
        ClassDeclarationSyntax type when type.ParameterList is { } primary => new OwnedSignature(type.Identifier, primary),
        StructDeclarationSyntax type when type.ParameterList is { } primary => new OwnedSignature(type.Identifier, primary),
        _ => null,
    };

    private static bool IsOwned(ExplicitInterfaceSpecifierSyntax? explicitInterface, SyntaxTokenList modifiers) =>
        explicitInterface is null
        && !modifiers.Any(SyntaxKind.OverrideKeyword)
        && !modifiers.Any(SyntaxKind.ExternKeyword);
}
