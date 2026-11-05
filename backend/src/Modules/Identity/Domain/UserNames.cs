namespace Starter.Modules.Identity.Domain;

/// <summary>
/// The one rule for comparing account identifiers (user name, e-mail, login name, UPN): trimmed and upper-cased
/// (invariant), so lookups are case-insensitive and the unique indexes hold the same form.
/// </summary>
public static class UserNames
{
    /// <summary>The normalized form of <paramref name="userName"/>.</summary>
    public static string Normalize(string userName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(userName);
        return userName.Trim().ToUpperInvariant();
    }

    /// <summary>The normalized form of an optional identifier; null when it is null or blank.</summary>
    public static string? NormalizeOptional(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : Normalize(value);

    /// <summary>An optional identifier as stored for display: trimmed; null when it is null or blank.</summary>
    public static string? TrimOptional(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
