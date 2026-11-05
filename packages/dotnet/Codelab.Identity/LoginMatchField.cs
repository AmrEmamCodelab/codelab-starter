namespace Codelab.Identity;

/// <summary>
/// Which value of a directory or OpenID Connect identity finds the local account a sign-in is for
/// (<c>MatchBy</c> in both provider sections). Stored and bound as an integer; every member has an explicit value.
/// </summary>
public enum LoginMatchField
{
    /// <summary>The email address (<c>mail</c> in the directory, the configured email claim in OIDC).</summary>
    Email = 1,

    /// <summary>The logon name (<c>sAMAccountName</c> in the directory, the configured login-name claim in OIDC).</summary>
    LoginName = 2,

    /// <summary>The user principal name (<c>userPrincipalName</c> in the directory, the configured upn claim in OIDC).</summary>
    Upn = 3,
}
