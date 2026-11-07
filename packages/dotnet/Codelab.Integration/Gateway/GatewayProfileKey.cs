using System.Text.RegularExpressions;

namespace Codelab.Integration.Gateway;

/// <summary>
/// The keys of the gateway relay profiles. A key names the profile in configuration
/// (<c>Codelab:Integration:Gateway:Profiles</c>) and is the second segment of its relay routes
/// (<c>/relay/{key}/...</c>, see <see cref="GatewayRoutes"/>).
/// </summary>
public static partial class GatewayProfileKey
{
    /// <summary>The OIDC back-channel relay: token exchange and JWKS.</summary>
    public const string Sso = "sso";

    /// <summary>The SMS relay to the client's SMS provider.</summary>
    public const string Sms = "sms";

    /// <summary>The longest profile key.</summary>
    public const int MaxLength = 32;

    /// <summary>
    /// True when <paramref name="key"/> is a well-formed profile key: lower-case ASCII letters, digits and hyphens,
    /// starting with a letter, at most <see cref="MaxLength"/> characters. Keys are compared ordinally.
    /// </summary>
    public static bool IsValid(string? key) => key is not null && KeyPattern().IsMatch(key);

    [GeneratedRegex("^[a-z][a-z0-9-]{0,31}$", RegexOptions.CultureInvariant)]
    private static partial Regex KeyPattern();
}
