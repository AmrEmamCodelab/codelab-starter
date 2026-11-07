namespace Starter.Modules.Ops.Domain;

/// <summary>
/// The shape of a reference code as Codelab.Observability mints it (<c>ReferenceCode.From</c>): the first ten hex
/// characters of the correlation id, uppercase, grouped five and five (<c>9C1E5-B7A20</c>). An operator may type it in
/// either case; the stored and compared form is uppercase.
/// </summary>
public static class ReferenceCodeFormat
{
    /// <summary>The length of each group.</summary>
    public const int GroupLength = 5;

    /// <summary>The separator between the groups.</summary>
    public const char Separator = '-';

    /// <summary>The uppercase code when <paramref name="raw"/> is a well-formed reference code, otherwise null.</summary>
    public static string? Normalize(string? raw)
    {
        var trimmed = raw?.Trim();
        if (trimmed is null || trimmed.Length != OpsLimits.ReferenceCodeLength || trimmed[GroupLength] != Separator)
        {
            return null;
        }

        var groups = trimmed.Remove(GroupLength, count: 1);
        return groups.All(char.IsAsciiHexDigit) ? trimmed.ToUpperInvariant() : null;
    }
}
