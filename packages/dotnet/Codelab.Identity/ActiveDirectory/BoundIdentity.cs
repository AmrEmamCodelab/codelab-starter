namespace Codelab.Identity.ActiveDirectory;

/// <summary>Which evidence tied the bound account to the found entry (logged with a refusal).</summary>
internal enum BoundIdentityCheck
{
    /// <summary>The connection could not ask "Who am I?"; the entry's <c>userPrincipalName</c> was compared with the bind name.</summary>
    UpnFallback = 1,

    /// <summary>A <c>u:</c> authorization identity was compared with the entry's logon name (and domain).</summary>
    LogonName = 2,

    /// <summary>A <c>dn:</c> authorization identity was compared with the entry's DN.</summary>
    DistinguishedName = 3,

    /// <summary>The directory answered an empty or unknown authorization identity.</summary>
    Unrecognised = 4,
}

/// <summary>
/// Ties the account the directory checked the password against to the entry the search found. The bind name and the
/// search filter are different strings: a bare <c>bob</c> binds as <c>bob@Domain</c>, which the directory resolves to
/// whichever account holds that UPN explicitly (possibly one in a child domain or outside <c>BaseDn</c>), while the search
/// finds the local <c>sAMAccountName=bob</c>. Without this check that sign-in would succeed as bob with the other
/// account's password.
/// </summary>
internal static class BoundIdentity
{
    private const string UserPrefix = "u:";
    private const string DistinguishedNamePrefix = "dn:";
    private const char DownLevelSeparator = '\\';
    private const char UpnSeparator = '@';

    /// <summary>
    /// Whether <paramref name="entry"/> is the bound account. <paramref name="authorizationId"/> is the RFC 4532 answer,
    /// or null when the connection cannot ask (then only the weaker <see cref="BoundIdentityCheck.UpnFallback"/> runs).
    /// </summary>
    internal static (bool Matches, BoundIdentityCheck Check) Verify(
        string? authorizationId, LdapEntry entry, DirectoryUserName name, ActiveDirectoryOptions settings)
    {
        ArgumentNullException.ThrowIfNull(entry);
        ArgumentNullException.ThrowIfNull(name);
        ArgumentNullException.ThrowIfNull(settings);
        if (authorizationId is null)
        {
            return (UpnConsistent(entry, name), BoundIdentityCheck.UpnFallback);
        }

        var value = authorizationId.Trim();
        if (value.StartsWith(DistinguishedNamePrefix, StringComparison.OrdinalIgnoreCase))
        {
            var dn = value[DistinguishedNamePrefix.Length..].Trim();
            return (dn.Length > 0 && string.Equals(dn, entry.DistinguishedName, StringComparison.OrdinalIgnoreCase), BoundIdentityCheck.DistinguishedName);
        }

        if (value.StartsWith(UserPrefix, StringComparison.OrdinalIgnoreCase))
        {
            return (UserMatches(value[UserPrefix.Length..], entry, name, settings), BoundIdentityCheck.LogonName);
        }

        return (false, BoundIdentityCheck.Unrecognised);
    }

    /// <summary>
    /// <c>DOMAIN\sam</c>: the logon name must be the entry's, and the domain must be <c>NetBiosName</c>. With no
    /// <c>NetBiosName</c> configured the domain cannot be checked, so the UPN comparison runs as well.
    /// </summary>
    private static bool UserMatches(string user, LdapEntry entry, DirectoryUserName name, ActiveDirectoryOptions settings)
    {
        var separator = user.IndexOf(DownLevelSeparator, StringComparison.Ordinal);
        var logonName = separator >= 0 ? user[(separator + 1)..] : user;
        if (logonName.Length == 0 || !SameLogonName(logonName, entry))
        {
            return false;
        }

        if (separator >= 0 && !string.IsNullOrWhiteSpace(settings.NetBiosName))
        {
            return string.Equals(user[..separator], settings.NetBiosName.Trim(), StringComparison.OrdinalIgnoreCase);
        }

        return UpnConsistent(entry, name);
    }

    private static bool SameLogonName(string logonName, LdapEntry entry) =>
        Same(logonName, entry.Attribute(ActiveDirectoryAttributes.LoginName))
        || Same(logonName, entry.Attribute(ActiveDirectoryAttributes.Upn));

    /// <summary>
    /// A UPN-form bind name and an entry that carries a <c>userPrincipalName</c> must agree. Absent UPN or a down-level
    /// bind name: nothing to compare, so this cannot refuse (the reason the RFC 4532 check is preferred).
    /// </summary>
    private static bool UpnConsistent(LdapEntry entry, DirectoryUserName name)
    {
        var upn = entry.Attribute(ActiveDirectoryAttributes.Upn);
        if (string.IsNullOrWhiteSpace(upn) || name.BindName.IndexOf(UpnSeparator, StringComparison.Ordinal) < 0)
        {
            return true;
        }

        return Same(upn, name.BindName);
    }

    private static bool Same(string value, string? other) =>
        other is not null && string.Equals(value.Trim(), other.Trim(), StringComparison.OrdinalIgnoreCase);
}
