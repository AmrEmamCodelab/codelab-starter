using Starter.Domain.Users;

namespace Starter.Modules.Identity.Domain;

/// <summary>How a session was signed in. Stored on the session and on auth events as the integer value.</summary>
public enum SignInMode
{
    /// <summary>User name and password checked by this module.</summary>
    Basic = 1,

    /// <summary>User name and password checked by the organization's Active Directory.</summary>
    ActiveDirectory = 2,

    /// <summary>The organization's OpenID Connect identity provider.</summary>
    Sso = 3,
}

/// <summary>The account field an external identity (directory or identity provider) is matched on.</summary>
public enum AccountMatchField
{
    /// <summary>The e-mail address.</summary>
    Email = 1,

    /// <summary>The directory login name (sAMAccountName style).</summary>
    LoginName = 2,

    /// <summary>The user principal name.</summary>
    Upn = 3,
}

/// <summary>The one rule that pairs an account's binding with the sign-in modes it accepts.</summary>
public static class AccountBindings
{
    /// <summary>
    /// True when an account bound to <paramref name="binding"/> may sign in through <paramref name="mode"/>: a
    /// password account through basic login only, a directory account through Active Directory only, an SSO account
    /// through the identity provider only.
    /// </summary>
    public static bool Accepts(AccountBinding binding, SignInMode mode) => (binding, mode) switch
    {
        (AccountBinding.Password, SignInMode.Basic) => true,
        (AccountBinding.ActiveDirectory, SignInMode.ActiveDirectory) => true,
        (AccountBinding.Sso, SignInMode.Sso) => true,
        _ => false,
    };
}
