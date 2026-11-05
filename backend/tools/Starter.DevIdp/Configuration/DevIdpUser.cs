namespace Starter.DevIdp.Configuration;

/// <summary>A fixture user the dev IdP can sign in. Use neutral data only (the <c>example.test</c> domain).</summary>
public sealed class DevIdpUser
{
    /// <summary>The stable subject (<c>sub</c>).</summary>
    public string Subject { get; set; } = string.Empty;

    /// <summary>The login name, sent as <c>preferred_username</c>.</summary>
    public string? LoginName { get; set; }

    /// <summary>The email address (<c>email</c>).</summary>
    public string? Email { get; set; }

    /// <summary>The user principal name (<c>upn</c>).</summary>
    public string? Upn { get; set; }

    /// <summary>The display name (<c>name</c>).</summary>
    public string? DisplayName { get; set; }

    /// <summary>
    /// Whether <paramref name="loginHint"/> names this user: the subject (ordinal), or the login name, email or UPN
    /// (ordinal, ignoring case).
    /// </summary>
    internal bool Matches(string loginHint) =>
        string.Equals(Subject, loginHint, StringComparison.Ordinal)
        || string.Equals(LoginName, loginHint, StringComparison.OrdinalIgnoreCase)
        || string.Equals(Email, loginHint, StringComparison.OrdinalIgnoreCase)
        || string.Equals(Upn, loginHint, StringComparison.OrdinalIgnoreCase);
}
