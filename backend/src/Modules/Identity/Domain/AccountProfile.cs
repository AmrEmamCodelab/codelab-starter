using Starter.Domain.Users;

namespace Starter.Modules.Identity.Domain;

/// <summary>The editable part of an account (everything but the user name, the subject and the credentials).</summary>
/// <param name="DisplayName">The name shown in the application.</param>
/// <param name="Email">The e-mail address, or null.</param>
/// <param name="LoginName">The directory login name, or null.</param>
/// <param name="Upn">The user principal name, or null.</param>
/// <param name="Kind">Internal or external.</param>
/// <param name="Binding">The one sign-in mode the account accepts.</param>
/// <param name="OrganizationId">The organization (internal id, owned by the Users module), or null.</param>
public sealed record AccountProfile(
    string DisplayName,
    string? Email,
    string? LoginName,
    string? Upn,
    UserKind Kind,
    AccountBinding Binding,
    long? OrganizationId);

/// <summary>What registering an administered account needs. It never carries a password.</summary>
/// <param name="SubjectId">A new random token subject.</param>
/// <param name="UserName">The user name.</param>
/// <param name="Profile">The editable fields.</param>
/// <param name="CreatedAt">The creation time.</param>
public sealed record AccountRegistration(Guid SubjectId, string UserName, AccountProfile Profile, DateTimeOffset CreatedAt);

/// <summary>What creating a configured account (break-glass, Development user) needs.</summary>
/// <param name="SubjectId">A new random token subject.</param>
/// <param name="UserName">The user name.</param>
/// <param name="DisplayName">The display name.</param>
/// <param name="PasswordHash">The password hash.</param>
/// <param name="IsBreakGlass">True for the break-glass administrator.</param>
/// <param name="CreatedAt">The creation time.</param>
public sealed record NewUserAccount(Guid SubjectId, string UserName, string DisplayName, string PasswordHash, bool IsBreakGlass, DateTimeOffset CreatedAt);

/// <summary>The lockout rule (from <c>Identity:Lockout</c>).</summary>
/// <param name="MaxFailedAttempts">Consecutive wrong passwords that lock the account (positive).</param>
/// <param name="Duration">How long a lockout lasts (positive).</param>
public sealed record LockoutPolicy(int MaxFailedAttempts, TimeSpan Duration);

/// <summary>Whether an account may be given an activation link, or why not.</summary>
public enum ActivationEligibility
{
    /// <summary>A link may be issued.</summary>
    Eligible = 1,

    /// <summary>The break-glass administrator's password comes from configuration only.</summary>
    BreakGlass = 2,

    /// <summary>The account is bound to Active Directory or SSO and holds no password.</summary>
    NotPasswordBound = 3,

    /// <summary>The account is deactivated.</summary>
    Inactive = 4,
}
