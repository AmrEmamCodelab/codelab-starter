using System.Text.RegularExpressions;

namespace Starter.Modules.Portal.Domain;

/// <summary>
/// The address shape the contact form and the inbox option accept: a local part, one at-sign and a dotted domain, with
/// no white space (the same pragmatic rule as the form's <c>EMAIL_SHAPE</c>; the mail relay has the final word).
/// </summary>
public static partial class EmailShape
{
    private const int MatchTimeoutMilliseconds = 250;

    /// <summary>True when <paramref name="address"/> has the accepted shape.</summary>
    public static bool IsWellFormed(string? address) => address is not null && Shape().IsMatch(address);

    [GeneratedRegex(@"^[^\s@]+@[^\s@]+\.[^\s@]+$", RegexOptions.CultureInvariant, MatchTimeoutMilliseconds)]
    private static partial Regex Shape();
}
