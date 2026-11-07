using Codelab.Observability;

namespace Codelab.OpsConsole.Application.ReferenceCodes;

/// <summary>
/// Turns what an operator pasted into the one reference-code form (Codelab.Observability <see cref="ReferenceCode"/>
/// owns the rule): a reference code in any case (<c>9c1e5-b7a20</c>) or the full 32-hex correlation id from a response
/// header both become <c>9C1E5-B7A20</c>. Anything else is refused, so only hex digits and the group separator ever
/// reach a log-store query.
/// </summary>
public static class ReferenceCodeInput
{
    /// <summary>The length of a reference code: two groups and the separator.</summary>
    public const int CodeLength = ReferenceCode.GroupLength + 1 + ReferenceCode.GroupLength;

    /// <summary>Normalizes <paramref name="input"/>; false when it is neither a reference code nor a correlation id.</summary>
    public static bool TryNormalize(string? input, out string referenceCode)
    {
        referenceCode = string.Empty;
        var trimmed = input?.Trim() ?? string.Empty;
        if (IsCorrelationId(trimmed))
        {
            referenceCode = ReferenceCode.From(trimmed.ToLowerInvariant());
            return true;
        }

        if (!IsReferenceCode(trimmed))
        {
            return false;
        }

        referenceCode = trimmed.ToUpperInvariant();
        return true;
    }

    private static bool IsCorrelationId(string value) =>
        value.Length == ReferenceCode.CorrelationIdLength && value.All(char.IsAsciiHexDigit);

    private static bool IsReferenceCode(string value)
    {
        if (value.Length != CodeLength || value[ReferenceCode.GroupLength] != ReferenceCode.GroupSeparator)
        {
            return false;
        }

        return value.AsSpan(0, ReferenceCode.GroupLength).ToString().All(char.IsAsciiHexDigit)
            && value.AsSpan(ReferenceCode.GroupLength + 1).ToString().All(char.IsAsciiHexDigit);
    }
}
