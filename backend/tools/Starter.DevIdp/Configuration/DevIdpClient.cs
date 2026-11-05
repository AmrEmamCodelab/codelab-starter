namespace Starter.DevIdp.Configuration;

/// <summary>A client registered with the dev IdP.</summary>
public sealed class DevIdpClient
{
    /// <summary>The <c>client_id</c> (and the <c>aud</c> of its id_tokens).</summary>
    public string ClientId { get; set; } = string.Empty;

    /// <summary>
    /// The client secret. Empty means a public client: it authenticates by PKCE only, and a presented secret is ignored.
    /// Never commit a value; tests generate one at run time.
    /// </summary>
    public string? ClientSecret { get; set; }

    /// <summary>The exact <c>redirect_uri</c> values the client may use (ordinal match, at least one).</summary>
    public IList<string> RedirectUris { get; } = new List<string>();

    /// <summary>The exact <c>post_logout_redirect_uri</c> values end-session may redirect to (ordinal match).</summary>
    public IList<string> PostLogoutRedirectUris { get; } = new List<string>();

    /// <summary>Whether the client is confidential (has a secret).</summary>
    internal bool IsConfidential => !string.IsNullOrEmpty(ClientSecret);

    /// <summary>Whether <paramref name="redirectUri"/> is registered exactly.</summary>
    internal bool AllowsRedirect(string? redirectUri) => Contains(RedirectUris, redirectUri);

    /// <summary>Whether <paramref name="postLogoutRedirectUri"/> is registered exactly.</summary>
    internal bool AllowsPostLogoutRedirect(string? postLogoutRedirectUri) => Contains(PostLogoutRedirectUris, postLogoutRedirectUri);

    private static bool Contains(IList<string> registered, string? candidate) =>
        candidate is not null && registered.Contains(candidate, StringComparer.Ordinal);
}
