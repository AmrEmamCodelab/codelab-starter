using System.Globalization;
using System.Text;

namespace Codelab.Identity.ActiveDirectory;

/// <summary>
/// A submitted user name in the two forms a sign-in needs: the bind name sent with the password, and the logon name
/// the search filter looks for. Accepts <c>user</c> (bound as <c>user@Domain</c>), <c>user@suffix</c> whose suffix is
/// <c>Domain</c> or one of <c>UpnSuffixes</c>, and <c>NETBIOS\user</c> whose prefix is <c>NetBiosName</c> (both bound as
/// typed).
/// <para>
/// <b>A name qualified with another domain is refused before the bind.</b> The search looks for the bare logon name under
/// the local <c>BaseDn</c>, so <c>OTHER\bob</c> (checked by the directory against the trusted domain's bob) would resolve
/// to the local bob and sign in as him.
/// </para>
/// </summary>
internal sealed record DirectoryUserName(string BindName, string LoginName)
{
    /// <summary>The longest user name accepted (the <c>userPrincipalName</c> limit is far below it).</summary>
    internal const int MaxLength = 256;

    private const char UpnSeparator = '@';
    private const char DownLevelSeparator = '\\';

    /// <summary>The outcome of <see cref="Parse"/>: the name, or why there is none.</summary>
    internal enum Verdict
    {
        Accepted = 1,
        Malformed = 2,
        ForeignDomain = 3,
    }

    /// <summary>
    /// The parsed name with its <see cref="Verdict"/>. <see cref="Verdict.Malformed"/>: blank, too long, a control
    /// character or no logon part. <see cref="Verdict.ForeignDomain"/>: a down-level prefix that is not
    /// <c>NetBiosName</c> (or none is configured), or a UPN suffix that is neither <c>Domain</c> nor in <c>UpnSuffixes</c>.
    /// </summary>
    internal static (Verdict Verdict, DirectoryUserName? Name) Parse(string? submitted, ActiveDirectoryOptions settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        var value = submitted?.Trim();
        if (string.IsNullOrEmpty(value) || value.Length > MaxLength || value.Any(char.IsControl))
        {
            return (Verdict.Malformed, null);
        }

        var downLevel = value.IndexOf(DownLevelSeparator, StringComparison.Ordinal);
        if (downLevel >= 0)
        {
            return Qualified(
                bindName: value,
                loginName: value[(downLevel + 1)..],
                isLocal: IsLocalNetBiosName(value[..downLevel], settings));
        }

        var at = value.IndexOf(UpnSeparator, StringComparison.Ordinal);
        if (at >= 0)
        {
            return Qualified(bindName: value, loginName: value[..at], isLocal: IsLocalUpnSuffix(value[(at + 1)..], settings));
        }

        return Named(bindName: string.Create(CultureInfo.InvariantCulture, $"{value}{UpnSeparator}{settings.Domain}"), loginName: value);
    }

    private static bool IsLocalNetBiosName(string prefix, ActiveDirectoryOptions settings) =>
        !string.IsNullOrWhiteSpace(settings.NetBiosName)
        && string.Equals(prefix, settings.NetBiosName.Trim(), StringComparison.OrdinalIgnoreCase);

    private static bool IsLocalUpnSuffix(string suffix, ActiveDirectoryOptions settings) =>
        SameSuffix(suffix, settings.Domain) || settings.UpnSuffixes.Any(allowed => SameSuffix(suffix, allowed));

    private static bool SameSuffix(string suffix, string? configured) =>
        !string.IsNullOrWhiteSpace(configured) && string.Equals(suffix, configured.Trim(), StringComparison.OrdinalIgnoreCase);

    /// <summary>A malformed logon part wins over a foreign domain: neither is sent, and malformed is the plainer reason.</summary>
    private static (Verdict Verdict, DirectoryUserName? Name) Qualified(string bindName, string loginName, bool isLocal)
    {
        var named = Named(bindName, loginName);
        return named.Verdict == Verdict.Accepted && !isLocal ? (Verdict.ForeignDomain, null) : named;
    }

    private static (Verdict Verdict, DirectoryUserName? Name) Named(string bindName, string loginName) =>
        string.IsNullOrWhiteSpace(loginName)
            ? (Verdict.Malformed, null)
            : (Verdict.Accepted, new DirectoryUserName(bindName, loginName));
}

/// <summary>RFC 4515 section 3 escaping for a value substituted into a search filter.</summary>
internal static class LdapFilter
{
    private const string HexFormat = "x2";
    private const char EscapePrefix = '\\';

    /// <summary>
    /// Escapes <c>\ * ( )</c>, NUL and every other control character as <c>\hh</c>. Not optional: the value is whatever an
    /// anonymous caller posted, and <c>*</c> alone would turn the filter into "every account".
    /// </summary>
    internal static string Escape(string value)
    {
        var escaped = new StringBuilder(value.Length);
        foreach (var character in value)
        {
            if (NeedsEscape(character))
            {
                escaped.Append(EscapePrefix).Append(((int)character).ToString(HexFormat, CultureInfo.InvariantCulture));
            }
            else
            {
                escaped.Append(character);
            }
        }

        return escaped.ToString();
    }

    /// <summary>The filter with the escaped logon name at {0} and the escaped bind name at {1}.</summary>
    internal static string Build(string template, DirectoryUserName name) =>
        string.Format(CultureInfo.InvariantCulture, template, Escape(name.LoginName), Escape(name.BindName));

    private static bool NeedsEscape(char character) =>
        character is '\\' or '*' or '(' or ')' || char.IsControl(character);
}
