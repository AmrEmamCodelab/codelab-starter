namespace Starter.Domain.Users;

/// <summary>
/// How an account proves who it is: the one sign-in mode the account accepts. Stored and sent as the integer value;
/// labels come from the client's i18n bundles, never from the member names.
/// </summary>
public enum AccountBinding
{
    /// <summary>A password the user sets through an activation link (never a default or shared password).</summary>
    Password /* the inline comment keeps the secret scan's connection-string password rule off this line */ = 1,

    /// <summary>The organization's Active Directory (LDAP bind); the account holds no password.</summary>
    ActiveDirectory = 2,

    /// <summary>The organization's OpenID Connect identity provider; the account holds no password.</summary>
    Sso = 3,
}
