using System.Globalization;

namespace Codelab.Identity.ActiveDirectory;

/// <summary>
/// The one owner of "which directory answer means what". Only <see cref="LdapResultCodes.InvalidCredentials"/> on a bind
/// is a verdict on the credentials; every other code, and every fault below LDAP, means the question went unanswered.
/// </summary>
internal static class LdapFailureMapping
{
    private const string NoResultCode = "none";

    private static readonly string[] DisabledSubCodes =
    [
        ActiveDirectoryBindSubCodes.AccountDisabled,
        ActiveDirectoryBindSubCodes.AccountExpired,
        ActiveDirectoryBindSubCodes.AccountLockedOut,
    ];

    /// <summary>
    /// The status of a failed bind. <see cref="LdapResultCodes.InvalidCredentials"/> with an Active Directory sub-code
    /// for a disabled, expired or locked-out account is <see cref="DirectoryAuthStatus.AccountDisabled"/>; without one
    /// it is <see cref="DirectoryAuthStatus.InvalidCredentials"/>; anything else is <see cref="DirectoryAuthStatus.Unavailable"/>.
    /// </summary>
    internal static DirectoryAuthStatus ForBind(LdapOperationException failure)
    {
        if (failure.ResultCode != LdapResultCodes.InvalidCredentials)
        {
            return DirectoryAuthStatus.Unavailable;
        }

        var subCode = SubCodeOf(failure.ServerMessage);
        return subCode is not null && DisabledSubCodes.Contains(subCode, StringComparer.OrdinalIgnoreCase)
            ? DirectoryAuthStatus.AccountDisabled
            : DirectoryAuthStatus.InvalidCredentials;
    }

    /// <summary>The hexadecimal sub-code after <see cref="ActiveDirectoryBindSubCodes.Marker"/>, or null.</summary>
    internal static string? SubCodeOf(string? serverMessage)
    {
        if (string.IsNullOrEmpty(serverMessage))
        {
            return null;
        }

        var start = serverMessage.IndexOf(ActiveDirectoryBindSubCodes.Marker, StringComparison.OrdinalIgnoreCase);
        if (start < 0)
        {
            return null;
        }

        var digits = new string(serverMessage[(start + ActiveDirectoryBindSubCodes.Marker.Length)..]
            .TakeWhile(Uri.IsHexDigit)
            .ToArray());
        return digits.Length == 0 ? null : digits;
    }

    /// <summary>Whether <c>userAccountControl</c> carries ACCOUNTDISABLE (0x2).</summary>
    internal static bool IsDisabled(string? userAccountControl) =>
        long.TryParse(userAccountControl, NumberStyles.Integer, CultureInfo.InvariantCulture, out var flags)
        && (flags & ActiveDirectoryAttributes.AccountDisabledFlag) != 0;

    /// <summary>The result code as text for a log line (<c>none</c> below LDAP).</summary>
    internal static string Describe(int? resultCode) =>
        resultCode?.ToString(CultureInfo.InvariantCulture) ?? NoResultCode;
}

/// <summary>The directory attributes the provider reads, and the one <c>userAccountControl</c> flag it checks.</summary>
public static class ActiveDirectoryAttributes
{
    /// <summary>The logon name.</summary>
    public const string LoginName = "sAMAccountName";

    /// <summary>The user principal name.</summary>
    public const string Upn = "userPrincipalName";

    /// <summary>The mail address.</summary>
    public const string Email = "mail";

    /// <summary>The display name.</summary>
    public const string DisplayName = "displayName";

    /// <summary>The account control flags.</summary>
    public const string UserAccountControl = "userAccountControl";

    /// <summary>ACCOUNTDISABLE in <see cref="UserAccountControl"/>.</summary>
    public const long AccountDisabledFlag = 2;

    /// <summary>Every attribute the search asks for, and nothing else.</summary>
    public static IReadOnlyList<string> Requested { get; } = [LoginName, Upn, Email, DisplayName, UserAccountControl];
}
