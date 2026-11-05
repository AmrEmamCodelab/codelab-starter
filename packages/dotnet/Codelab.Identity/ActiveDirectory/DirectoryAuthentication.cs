namespace Codelab.Identity.ActiveDirectory;

/// <summary>The outcome of one directory sign-in. Stored and logged as an integer.</summary>
public enum DirectoryAuthStatus
{
    /// <summary>The directory bound the credentials and returned exactly one entry.</summary>
    Succeeded = 1,

    /// <summary>The directory refused the credentials, or the user could not be identified as exactly one entry.</summary>
    InvalidCredentials = 2,

    /// <summary>The directory knows the account and it is disabled, expired or locked out.</summary>
    AccountDisabled = 3,

    /// <summary>The directory could not be asked (unreachable, TLS refused, timed out, provider off). No password was checked.</summary>
    Unavailable = 4,
}

/// <summary>What the directory says about a signed-in user. Every value is trimmed; absent attributes are null.</summary>
/// <param name="LoginName">The logon name (<c>sAMAccountName</c>); the submitted name when the attribute is absent.</param>
/// <param name="Upn">The user principal name (<c>userPrincipalName</c>).</param>
/// <param name="Email">The mail address (<c>mail</c>).</param>
/// <param name="DisplayName">The display name (<c>displayName</c>).</param>
public sealed record DirectoryIdentity(string LoginName, string? Upn, string? Email, string? DisplayName);

/// <summary>The status of a directory sign-in and, only when it succeeded, the identity.</summary>
public sealed record DirectoryAuthResult(DirectoryAuthStatus Status, DirectoryIdentity? Identity)
{
    /// <summary>A refused sign-in: wrong password, unknown or ambiguous user.</summary>
    public static DirectoryAuthResult InvalidCredentials { get; } = new(DirectoryAuthStatus.InvalidCredentials, Identity: null);

    /// <summary>A disabled, expired or locked-out account.</summary>
    public static DirectoryAuthResult AccountDisabled { get; } = new(DirectoryAuthStatus.AccountDisabled, Identity: null);

    /// <summary>The directory could not be asked.</summary>
    public static DirectoryAuthResult Unavailable { get; } = new(DirectoryAuthStatus.Unavailable, Identity: null);

    /// <summary>A successful sign-in for <paramref name="identity"/>.</summary>
    public static DirectoryAuthResult Succeeded(DirectoryIdentity identity)
    {
        ArgumentNullException.ThrowIfNull(identity);
        return new DirectoryAuthResult(DirectoryAuthStatus.Succeeded, identity);
    }
}

/// <summary>Signs a user in against Active Directory: bind as the user, then read the user's own entry.</summary>
public interface IActiveDirectoryAuthenticator
{
    /// <summary>
    /// Checks <paramref name="userName"/> (a bare logon name, <c>user@domain</c> or <c>DOMAIN\user</c>) and
    /// <paramref name="password"/>. Never throws for a directory fault: that is <see cref="DirectoryAuthStatus.Unavailable"/>.
    /// A blank password is refused before anything is sent (an empty simple bind is an anonymous bind).
    /// Cancellation of <paramref name="cancellationToken"/> propagates.
    /// </summary>
    Task<DirectoryAuthResult> AuthenticateAsync(string userName, string password, CancellationToken cancellationToken);
}
