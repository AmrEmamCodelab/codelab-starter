using System.Text.RegularExpressions;

namespace Starter.Modules.Access.Domain;

/// <summary>
/// The shape of a role code: lower-case kebab case (<c>request-reviewers</c>), at most
/// <see cref="AccessLimits.RoleCodeMaxLength"/> characters. The code is the role's stable identity in configuration and
/// logs; it never changes after creation, so the screens show the bilingual names instead.
/// </summary>
public static partial class RoleCodeFormat
{
    /// <summary>The pattern a code must match (whole string).</summary>
    public const string Pattern = "^[a-z][a-z0-9]*(-[a-z0-9]+)*$";

    /// <summary>True when <paramref name="code"/> is a well-formed role code.</summary>
    public static bool IsValid(string? code) =>
        !string.IsNullOrEmpty(code) && code.Length <= AccessLimits.RoleCodeMaxLength && CodeRegex().IsMatch(code);

    [GeneratedRegex(Pattern, RegexOptions.CultureInvariant)]
    private static partial Regex CodeRegex();
}
