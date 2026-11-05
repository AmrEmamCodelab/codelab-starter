namespace Codelab.Identity.ActiveDirectory;

/// <summary>The LDAP result codes (RFC 4511 4.1.9, plus the client-side codes of the LDAP C API) this package maps.</summary>
public static class LdapResultCodes
{
    /// <summary><c>timeLimitExceeded</c>.</summary>
    public const int TimeLimitExceeded = 3;

    /// <summary><c>strongerAuthRequired</c>: the directory refuses a simple bind on an unprotected connection.</summary>
    public const int StrongerAuthRequired = 8;

    /// <summary><c>invalidCredentials</c>: the ONE code that means a password was checked and refused.</summary>
    public const int InvalidCredentials = 49;

    /// <summary><c>busy</c>.</summary>
    public const int Busy = 51;

    /// <summary><c>unavailable</c>.</summary>
    public const int Unavailable = 52;

    /// <summary>Client side: the server is down or unreachable.</summary>
    public const int ServerDown = 81;

    /// <summary>Client side: the operation timed out.</summary>
    public const int Timeout = 85;

    /// <summary>Client side: the connection could not be made.</summary>
    public const int ConnectError = 91;
}

/// <summary>
/// The Active Directory sub-codes carried in the diagnostic message of an <see cref="LdapResultCodes.InvalidCredentials"/>
/// answer (<c>... data 533, ...</c>). They tell a disabled account from a wrong password.
/// </summary>
public static class ActiveDirectoryBindSubCodes
{
    /// <summary>The prefix the sub-code follows in the server message.</summary>
    public const string Marker = "data ";

    /// <summary>The account is disabled.</summary>
    public const string AccountDisabled = "533";

    /// <summary>The account has expired.</summary>
    public const string AccountExpired = "701";

    /// <summary>The account is locked out.</summary>
    public const string AccountLockedOut = "775";
}
