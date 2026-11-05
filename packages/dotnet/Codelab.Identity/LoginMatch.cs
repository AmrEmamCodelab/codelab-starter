using Codelab.Identity.ActiveDirectory;
using Codelab.Identity.Oidc;

namespace Codelab.Identity;

/// <summary>
/// Selects the value a sign-in is matched on (<see cref="LoginMatchField"/>) from a provider's identity. The one owner
/// of the field-to-value mapping, so the Active Directory and the OIDC sign-in match the same way.
/// </summary>
/// <remarks>
/// The value is trimmed and is null when the provider did not release it (or released a blank value): a sign-in with
/// no match value must be refused by the caller, never matched against an empty column. Case folding is the caller's:
/// it must compare with the same normalization it stores.
/// </remarks>
public static class LoginMatch
{
    /// <summary>The <paramref name="field"/> value of a directory identity, or null when it has none.</summary>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="field"/> is not a defined member.</exception>
    public static string? ValueOf(DirectoryIdentity identity, LoginMatchField field)
    {
        ArgumentNullException.ThrowIfNull(identity);
        return Select(field, new MatchValues(identity.Email, identity.LoginName, identity.Upn));
    }

    /// <summary>The <paramref name="field"/> value of an OIDC identity, or null when it has none.</summary>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="field"/> is not a defined member.</exception>
    public static string? ValueOf(OidcIdentity identity, LoginMatchField field)
    {
        ArgumentNullException.ThrowIfNull(identity);
        return Select(field, new MatchValues(identity.Email, identity.LoginName, identity.Upn));
    }

    private static string? Select(LoginMatchField field, MatchValues values) => field switch
    {
        LoginMatchField.Email => Clean(values.Email),
        LoginMatchField.LoginName => Clean(values.LoginName),
        LoginMatchField.Upn => Clean(values.Upn),
        _ => throw new ArgumentOutOfRangeException(nameof(field), field, "Not a defined login match field."),
    };

    private static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private readonly record struct MatchValues(string? Email, string? LoginName, string? Upn);
}
