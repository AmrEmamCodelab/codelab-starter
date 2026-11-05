using Starter.Modules.Identity.Application;

namespace Starter.Modules.Identity.Infrastructure.Options;

/// <summary>The session lifetimes (section <c>Identity:Session</c>).</summary>
public sealed class SessionOptions
{
    /// <summary>The configuration section.</summary>
    public const string SectionName = "Identity:Session";

    /// <summary>The default access token lifetime, in minutes.</summary>
    public const int DefaultAccessTokenMinutes = 15;

    /// <summary>The default absolute session lifetime, in days.</summary>
    public const int DefaultRefreshTokenDays = 7;

    /// <summary>The default idle timeout, in minutes.</summary>
    public const int DefaultIdleTimeoutMinutes = 30;

    /// <summary>How long an access token is valid, in minutes (positive, at most the idle timeout).</summary>
    public int AccessTokenMinutes { get; set; } = DefaultAccessTokenMinutes;

    /// <summary>The absolute lifetime of a session, in days (positive); a refresh never extends it.</summary>
    public int RefreshTokenDays { get; set; } = DefaultRefreshTokenDays;

    /// <summary>How long a session may go without a refresh, in minutes (positive); the client signs out after it too.</summary>
    public int IdleTimeoutMinutes { get; set; } = DefaultIdleTimeoutMinutes;

    /// <summary>The Application view of these settings.</summary>
    public SessionSettings ToSettings() => new(
        AccessTokenLifetime: TimeSpan.FromMinutes(AccessTokenMinutes),
        RefreshTokenLifetime: TimeSpan.FromDays(RefreshTokenDays),
        IdleTimeout: TimeSpan.FromMinutes(IdleTimeoutMinutes));
}

/// <summary>
/// The access token signature and audience (section <c>Identity:Jwt</c>). The signing key comes only from the local
/// secrets file or the environment; issuer and audience default to the host's service name (<c>starter-api</c>).
/// </summary>
public sealed class JwtOptions
{
    /// <summary>The configuration section.</summary>
    public const string SectionName = "Identity:Jwt";

    /// <summary>The fewest bytes the HMAC-SHA256 signing key may have.</summary>
    public const int MinimumSigningKeyBytes = 32;

    /// <summary>The token issuer (<c>iss</c>); empty means the host's service name.</summary>
    public string? Issuer { get; set; }

    /// <summary>The token audience (<c>aud</c>); empty means the host's service name.</summary>
    public string? Audience { get; set; }

    /// <summary>The HMAC-SHA256 signing key, base64, at least <see cref="MinimumSigningKeyBytes"/> bytes.</summary>
    public string? SigningKey { get; set; }

    /// <summary>A log-safe description: the key is never printed.</summary>
    public override string ToString() => nameof(JwtOptions) + "(" + Issuer + ", " + Audience + ")";
}

/// <summary>The break-glass administrator (section <c>Identity:BreakGlass</c>); it holds every permission.</summary>
public sealed class BreakGlassOptions
{
    /// <summary>The configuration section.</summary>
    public const string SectionName = "Identity:BreakGlass";

    /// <summary>The default user name.</summary>
    public const string DefaultUserName = "admin";

    /// <summary>The default display name.</summary>
    public const string DefaultDisplayName = "Administrator";

    /// <summary>The user name.</summary>
    public string UserName { get; set; } = DefaultUserName;

    /// <summary>The display name.</summary>
    public string DisplayName { get; set; } = DefaultDisplayName;

    /// <summary>The password: only from the local secrets file or the environment. Required; the host does not start without it.</summary>
    public string? Password { get; set; }

    /// <summary>A log-safe description: the password is never printed.</summary>
    public override string ToString() => nameof(BreakGlassOptions) + "(" + UserName + ")";
}

/// <summary>
/// The Development users (section <c>Identity:DevUsers</c>, an array): low-privilege accounts that hold only the
/// dashboard. Seeded in Development ONLY; any other environment ignores the section.
/// </summary>
public sealed class DevUsersOptions
{
    /// <summary>The configuration section.</summary>
    public const string SectionName = "Identity:DevUsers";

    /// <summary>The users.</summary>
    public IList<DevUserOptions> Users { get; } = [];
}

/// <summary>One Development user (<c>Identity:DevUsers:&lt;n&gt;</c>).</summary>
public sealed class DevUserOptions
{
    /// <summary>The user name.</summary>
    public string? UserName { get; set; }

    /// <summary>The display name (the user name when empty).</summary>
    public string? DisplayName { get; set; }

    /// <summary>The password (local secrets file only).</summary>
    public string? Password { get; set; }

    /// <summary>A log-safe description: the password is never printed.</summary>
    public override string ToString() => nameof(DevUserOptions) + "(" + UserName + ")";
}
