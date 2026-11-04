using System.Globalization;
using System.Security.Claims;
using Codelab.Security.ServiceAuth;
using Codelab.Security.Signing;
using Codelab.Security.Storage;

namespace Codelab.Security.Sessions;

/// <summary>The claim types the session issuer writes and the security controls read.</summary>
public static class SecurityClaimTypes
{
    /// <summary>The JWT subject.</summary>
    public const string Subject = "sub";

    /// <summary>The session id (one per login).</summary>
    public const string SessionId = "sid";

    /// <summary>The signing master key id the session key is derived from.</summary>
    public const string SessionKeyId = "skid";

    /// <summary>The user's session version when the token was issued.</summary>
    public const string SessionVersion = "sver";

    /// <summary>The calling service of a service-to-service assertion.</summary>
    public const string Service = "codelab_service";
}

/// <summary>
/// Sessions (<c>Codelab:Security:Sessions</c>): which claims carry the user, the session, the signing key id and the
/// session version, and how revocation is checked on every request.
/// </summary>
public sealed class SecuritySessionOptions
{
    /// <summary>The configuration section.</summary>
    public const string SectionName = SecuritySections.Sessions;

    /// <summary>Claim types for the user id, first non-empty wins.</summary>
    public IList<string> UserIdClaimTypes { get; init; } = [SecurityClaimTypes.Subject, ClaimTypes.NameIdentifier];

    /// <summary>The session id claim.</summary>
    public string SessionIdClaimType { get; set; } = SecurityClaimTypes.SessionId;

    /// <summary>The signing key id claim.</summary>
    public string SessionKeyIdClaimType { get; set; } = SecurityClaimTypes.SessionKeyId;

    /// <summary>The session version claim.</summary>
    public string SessionVersionClaimType { get; set; } = SecurityClaimTypes.SessionVersion;

    /// <summary>Check the sid revocation list and the session version on every authenticated request (default on).</summary>
    public bool ValidateOnEveryRequest { get; set; } = true;

    /// <summary>Refuse authenticated user tokens that carry no session id (default on: such a token cannot be revoked).</summary>
    public bool RequireSessionId { get; set; } = true;

    /// <summary>Memory (default, single process) or SqlServer (shared, uses <c>Codelab:Security:SqlStore</c>).</summary>
    public SecurityStoreKind Store { get; set; } = SecurityStoreKind.Memory;
}

/// <summary>Reads the session claims of a principal under <see cref="SecuritySessionOptions"/>.</summary>
public static class SessionClaims
{
    /// <summary>The first non-empty value of any of <paramref name="types"/>, or empty.</summary>
    public static string Value(ClaimsPrincipal user, IEnumerable<string> types)
    {
        ArgumentNullException.ThrowIfNull(user);
        ArgumentNullException.ThrowIfNull(types);
        return types.Select(type => user.FindFirst(type)?.Value).FirstOrDefault(value => !string.IsNullOrWhiteSpace(value)) ?? string.Empty;
    }

    /// <summary>The user id.</summary>
    public static string UserId(ClaimsPrincipal user, SecuritySessionOptions options) =>
        Value(user, options.UserIdClaimTypes);

    /// <summary>The session identity, or null when the user id or the session id is missing.</summary>
    public static SessionIdentity? Identity(ClaimsPrincipal user, SecuritySessionOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        var userId = UserId(user, options);
        var sessionId = Value(user, [options.SessionIdClaimType]);
        return userId.Length == 0 || sessionId.Length == 0 ? null : new SessionIdentity(userId, sessionId);
    }

    /// <summary>The session version, when the claim is present and numeric.</summary>
    public static long? Version(ClaimsPrincipal user, SecuritySessionOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        var raw = Value(user, [options.SessionVersionClaimType]);
        return long.TryParse(raw, NumberStyles.None, CultureInfo.InvariantCulture, out var version) ? version : null;
    }

    /// <summary>
    /// True for a service-to-service principal (session checks and request signing do not apply): every authenticated
    /// identity was issued by the service-assertion scheme and carries <see cref="SecurityClaimTypes.Service"/>. The
    /// claim alone proves nothing: a user token that carries a <c>codelab_service</c> claim (copied from an external
    /// identity provider, say) is still a user, and a principal merged from a user and a service identity is a user.
    /// </summary>
    public static bool IsService(ClaimsPrincipal user)
    {
        ArgumentNullException.ThrowIfNull(user);
        var authenticated = user.Identities.Where(identity => identity.IsAuthenticated).ToList();
        return authenticated.Count > 0 && authenticated.All(IsServiceIdentity);
    }

    private static bool IsServiceIdentity(ClaimsIdentity identity) =>
        string.Equals(identity.AuthenticationType, ServiceAssertionAuthenticationHandler.SchemeName, StringComparison.Ordinal)
        && identity.HasClaim(claim => claim.Type == SecurityClaimTypes.Service);
}
