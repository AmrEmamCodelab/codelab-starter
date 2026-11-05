namespace Starter.DevIdp.Configuration;

/// <summary>
/// The dev IdP settings (section <see cref="SectionName"/>): the issuer it signs as, the registered clients and the
/// fixture users it can sign in. Checked at start by <see cref="DevIdpOptionsValidator"/>.
/// </summary>
public sealed class DevIdpOptions
{
    /// <summary>The configuration section.</summary>
    public const string SectionName = "DevIdp";

    /// <summary>The default issuer, which is also the default listen URL.</summary>
    public const string DefaultIssuer = "http://localhost:5190";

    /// <summary>The default lifetime of an authorization code, in seconds.</summary>
    public const int DefaultCodeLifetimeSeconds = 60;

    /// <summary>The default lifetime of an id_token and an access token, in seconds.</summary>
    public const int DefaultTokenLifetimeSeconds = 300;

    /// <summary>The issuer (<c>iss</c>) and the base of every endpoint URL: an absolute http(s) URL with no path.</summary>
    public string Issuer { get; set; } = DefaultIssuer;

    /// <summary>How long an authorization code can be redeemed, in seconds (at least one).</summary>
    public int CodeLifetimeSeconds { get; set; } = DefaultCodeLifetimeSeconds;

    /// <summary>How long an id_token and an access token are valid, in seconds (at least one).</summary>
    public int TokenLifetimeSeconds { get; set; } = DefaultTokenLifetimeSeconds;

    /// <summary>The registered clients (at least one).</summary>
    public IList<DevIdpClient> Clients { get; } = new List<DevIdpClient>();

    /// <summary>The fixture users (at least one).</summary>
    public IList<DevIdpUser> Users { get; } = new List<DevIdpUser>();

    /// <summary>The issuer without a trailing slash, the base every endpoint URL is built on.</summary>
    internal string IssuerBase => Issuer.TrimEnd('/');

    /// <summary>The client registered as <paramref name="clientId"/> (ordinal), or <see langword="null"/>.</summary>
    internal DevIdpClient? FindClient(string? clientId) =>
        clientId is null ? null : Clients.FirstOrDefault(client => string.Equals(client.ClientId, clientId, StringComparison.Ordinal));

    /// <summary>The fixture user whose subject, login name, email or UPN is <paramref name="loginHint"/>, or <see langword="null"/>.</summary>
    internal DevIdpUser? FindUser(string? loginHint) =>
        loginHint is null ? null : Users.FirstOrDefault(user => user.Matches(loginHint));
}
