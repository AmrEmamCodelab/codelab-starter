using System.Buffers.Text;
using System.Security.Cryptography;
using System.Text;

namespace Starter.Modules.Identity.Application.LoginModes;

/// <summary>
/// Which return path an SSO sign-in may send the browser to after it: a root-relative path of the SPA only. Anything
/// else (an absolute or scheme-relative URL, a backslash, a control character, an over-long value) becomes
/// <see cref="Root"/>, so the flow can never be turned into an open redirect.
/// </summary>
public static class ReturnUrlPolicy
{
    /// <summary>The path used when no safe return path was given.</summary>
    public const string Root = "/";

    private const char Slash = '/';
    private const char Backslash = '\\';

    /// <summary><paramref name="returnUrl"/> when it is a safe root-relative path; otherwise <see cref="Root"/>.</summary>
    public static string Sanitize(string? returnUrl) => IsSafe(returnUrl) ? returnUrl! : Root;

    /// <summary>True for a root-relative path such as <c>/admin/users?page=2</c>.</summary>
    public static bool IsSafe(string? returnUrl) =>
        !string.IsNullOrEmpty(returnUrl)
        && returnUrl.Length <= SsoLimits.ReturnUrlMaxLength
        && returnUrl[0] == Slash
        && (returnUrl.Length == 1 || (returnUrl[1] != Slash && returnUrl[1] != Backslash))
        && !returnUrl.Contains(Backslash, StringComparison.Ordinal)
        && !returnUrl.Any(char.IsControl);
}

/// <summary>
/// The browser binding of an SSO sign-in (required): the SPA keeps a random secret in its tab and sends only its
/// base64url SHA-256 to <c>sso/login</c>; the exchange must present the secret. A ticket link forwarded to another
/// browser therefore cannot sign that browser in (login CSRF). A sign-in without a binding is refused at
/// <c>sso/login</c>, and a ticket that carries none is refused at the exchange (fail closed).
/// </summary>
public static class SsoBinding
{
    /// <summary>True when <paramref name="hash"/> is shaped like a base64url SHA-256 (43 characters, no padding).</summary>
    public static bool IsHashShaped(string? hash) =>
        hash is { Length: SsoLimits.BindingHashLength } && Base64Url.IsValid(hash, out var decodedLength) && decodedLength == SHA256.HashSizeInBytes;

    /// <summary>The base64url SHA-256 of <paramref name="proof"/>.</summary>
    public static string HashOf(string proof)
    {
        ArgumentNullException.ThrowIfNull(proof);
        return Base64Url.EncodeToString(SHA256.HashData(Encoding.UTF8.GetBytes(proof)));
    }

    /// <summary>
    /// True when the exchange may proceed: the sign-in carried a binding and <paramref name="proof"/> hashes to it
    /// (compared in constant time). A ticket without a binding is never accepted.
    /// </summary>
    public static bool Accepts(string? bindingHash, string? proof)
    {
        if (bindingHash is null || string.IsNullOrEmpty(proof))
        {
            return false;
        }

        return CryptographicOperations.FixedTimeEquals(left: Encoding.ASCII.GetBytes(HashOf(proof)), right: Encoding.ASCII.GetBytes(bindingHash));
    }
}
