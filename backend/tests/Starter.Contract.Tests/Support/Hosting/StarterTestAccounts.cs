using System.Security.Cryptography;

namespace Starter.Tests.Hosting;

/// <summary>
/// The accounts and key material a test host runs with, generated fresh at run time (never fixture values, never the
/// developer's local secrets file, never committed). Shared by the contract and integration tests.
/// </summary>
/// <param name="BreakGlassUserName">The break-glass administrator's user name.</param>
/// <param name="BreakGlassPassword">The break-glass administrator's password.</param>
/// <param name="DevUserName">The Development-only low-privilege user's name.</param>
/// <param name="DevUserPassword">The Development-only user's password.</param>
public sealed record StarterTestAccounts(string BreakGlassUserName, string BreakGlassPassword, string DevUserName, string DevUserPassword)
{
    /// <summary>The number of random bytes in every generated key.</summary>
    public const int KeyBytes = 32;

    private const int PasswordRandomBytes = 18;
    private const int UserNameRandomBytes = 4;
    private const int KeyIdRandomBytes = 3; // key ids are at most 8 characters [a-z0-9]: the prefix plus 6 hex digits
    private const string PasswordComplexitySuffix = "Aa1!";
    private const string KeyIdPrefix = "t";
    private const string BreakGlassUserNamePrefix = "it-admin-";
    private const string DevUserNamePrefix = "it-dev-";

    /// <summary>Fresh random accounts (user names are unique per run, so parallel hosts never collide).</summary>
    public static StarterTestAccounts Generate()
    {
        var suffix = Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(UserNameRandomBytes));
        return new StarterTestAccounts(
            BreakGlassUserName: BreakGlassUserNamePrefix + suffix,
            BreakGlassPassword: NewPassword(),
            DevUserName: DevUserNamePrefix + suffix,
            DevUserPassword: NewPassword());
    }

    /// <summary>A fresh base64 key of <see cref="KeyBytes"/> random bytes.</summary>
    public static string NewKeyBase64() => Convert.ToBase64String(RandomNumberGenerator.GetBytes(KeyBytes));

    /// <summary>A fresh key id.</summary>
    public static string NewKeyId() => KeyIdPrefix + Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(KeyIdRandomBytes));

    private static string NewPassword() =>
        Convert.ToBase64String(RandomNumberGenerator.GetBytes(PasswordRandomBytes)).Replace('+', '-').Replace('/', '_') + PasswordComplexitySuffix;
}
