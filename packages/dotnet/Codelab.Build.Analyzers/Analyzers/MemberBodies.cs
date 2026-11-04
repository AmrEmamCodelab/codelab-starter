using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Codelab.Build.Analyzers.Analyzers;

/// <summary>The body, display name and report location of a member whose length CLB1001 measures.</summary>
internal sealed class MemberBody
{
    public MemberBody(SyntaxNode body, string name, Location location)
    {
        Body = body;
        Name = name;
        Location = location;
    }

    public SyntaxNode Body { get; }

    public string Name { get; }

    public Location Location { get; }
}

/// <summary>Maps a member declaration to its measured body; null when the member has no body (abstract, auto-property).</summary>
internal static class MemberBodies
{
    private const string ConstructorName = "constructor";
    private const string FinalizerName = "finalizer";
    private const string OperatorPrefix = "operator ";
    private const string AccessorSeparator = ".";

    public static MemberBody? Describe(SyntaxNode node) => node switch
    {
        MethodDeclarationSyntax method => Create(Body(method.Body, method.ExpressionBody), method.Identifier.ValueText, method.Identifier.GetLocation()),
        ConstructorDeclarationSyntax ctor => Create(Body(ctor.Body, ctor.ExpressionBody), $"{ctor.Identifier.ValueText} {ConstructorName}", ctor.Identifier.GetLocation()),
        DestructorDeclarationSyntax dtor => Create(Body(dtor.Body, dtor.ExpressionBody), $"{dtor.Identifier.ValueText} {FinalizerName}", dtor.Identifier.GetLocation()),
        OperatorDeclarationSyntax op => Create(Body(op.Body, op.ExpressionBody), OperatorPrefix + op.OperatorToken.ValueText, op.OperatorToken.GetLocation()),
        ConversionOperatorDeclarationSyntax conversion => Create(Body(conversion.Body, conversion.ExpressionBody), OperatorPrefix + conversion.Type, conversion.Type.GetLocation()),
        LocalFunctionStatementSyntax local => Create(Body(local.Body, local.ExpressionBody), local.Identifier.ValueText, local.Identifier.GetLocation()),
        AccessorDeclarationSyntax accessor => Create(Body(accessor.Body, accessor.ExpressionBody), AccessorName(accessor), accessor.Keyword.GetLocation()),
        PropertyDeclarationSyntax property => Create(property.ExpressionBody, property.Identifier.ValueText, property.Identifier.GetLocation()),
        IndexerDeclarationSyntax indexer => Create(indexer.ExpressionBody, indexer.ThisKeyword.ValueText, indexer.ThisKeyword.GetLocation()),
        _ => null,
    };

    private static SyntaxNode? Body(BlockSyntax? block, ArrowExpressionClauseSyntax? expression) =>
        (SyntaxNode?)block ?? expression;

    private static MemberBody? Create(SyntaxNode? body, string name, Location location) =>
        body is null ? null : new MemberBody(body, name, location);

    private static string AccessorName(AccessorDeclarationSyntax accessor)
    {
        var owner = accessor.Parent?.Parent switch
        {
            PropertyDeclarationSyntax property => property.Identifier.ValueText,
            EventDeclarationSyntax eventDeclaration => eventDeclaration.Identifier.ValueText,
            IndexerDeclarationSyntax indexer => indexer.ThisKeyword.ValueText,
            _ => string.Empty,
        };
        return owner + AccessorSeparator + accessor.Keyword.ValueText;
    }
}
