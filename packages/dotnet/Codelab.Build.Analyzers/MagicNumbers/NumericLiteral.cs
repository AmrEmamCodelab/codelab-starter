using System.Globalization;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Codelab.Build.Analyzers.MagicNumbers;

/// <summary>A numeric literal with its sign applied: "-1" is one literal, reported once on the whole expression.</summary>
internal sealed class NumericLiteral
{
    private NumericLiteral(ExpressionSyntax expression, double value)
    {
        Expression = expression;
        Value = value;
    }

    /// <summary>The literal, or the unary minus expression around it.</summary>
    public ExpressionSyntax Expression { get; }

    public double Value { get; }

    public string Text => Expression.ToString();

    /// <summary>Null when the literal has no constant value (a syntax error the compiler reports anyway).</summary>
    public static NumericLiteral? From(LiteralExpressionSyntax literal)
    {
        if (literal.Token.Value is not { } raw)
        {
            return null;
        }

        var value = Convert.ToDouble(raw, CultureInfo.InvariantCulture);
        if (literal.Parent is PrefixUnaryExpressionSyntax unary && unary.IsKind(SyntaxKind.UnaryMinusExpression))
        {
            return new NumericLiteral(unary, -value);
        }

        return new NumericLiteral(literal, value);
    }
}
