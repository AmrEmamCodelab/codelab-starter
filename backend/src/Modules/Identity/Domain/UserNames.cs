namespace Starter.Modules.Identity.Domain;

/// <summary>The one rule for comparing user names: trimmed and upper-cased (invariant), so lookups are case-insensitive.</summary>
public static class UserNames
{
    /// <summary>The normalized form of <paramref name="userName"/>.</summary>
    public static string Normalize(string userName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(userName);
        return userName.Trim().ToUpperInvariant();
    }
}
