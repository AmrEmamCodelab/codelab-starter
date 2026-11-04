using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Codelab.Build.Analyzers.MagicNumbers;

/// <summary>
/// Decides whether a literal sits where the code already gives it a name, so it is not a magic number: a const
/// declaration, the initializer of a const or readonly field, a property initializer, an enum member value, an
/// attribute argument or a parameter default value. Anything inside such an initializer counts as named
/// (<c>TimeSpan.FromSeconds(30)</c> in a static readonly field).
/// </summary>
internal static class NamingContext
{
    public static bool IsNamed(SyntaxNode literal)
    {
        foreach (var ancestor in literal.Ancestors())
        {
            switch (ancestor)
            {
                case AttributeArgumentSyntax:
                case EnumMemberDeclarationSyntax:
                case EqualsValueClauseSyntax { Parent: PropertyDeclarationSyntax or ParameterSyntax }:
                    return true;
                case LocalDeclarationStatementSyntax local:
                    return local.IsConst;
                case FieldDeclarationSyntax field:
                    return field.Modifiers.Any(SyntaxKind.ConstKeyword) || field.Modifiers.Any(SyntaxKind.ReadOnlyKeyword);
                case MemberDeclarationSyntax:
                    return false;
            }
        }

        return false;
    }
}
