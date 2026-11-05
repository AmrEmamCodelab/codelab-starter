using Starter.Application.Results;
using Starter.Domain.Users;

namespace Starter.Application.Users;

/// <summary>An account as the administration screens see it. Never carries a password, hash or ticket.</summary>
/// <param name="UserId">The internal user id (the wire carries its PublicId).</param>
/// <param name="SubjectId">The token subject.</param>
/// <param name="UserName">The sign-in name, as entered.</param>
/// <param name="DisplayName">The name shown in the shell and the lists.</param>
/// <param name="Email">The e-mail address, or null.</param>
/// <param name="LoginName">The directory login name (Active Directory sAMAccountName style), or null.</param>
/// <param name="Upn">The user principal name, or null.</param>
/// <param name="Kind">Internal or external.</param>
/// <param name="Binding">The one sign-in mode the account accepts.</param>
/// <param name="OrganizationId">The organization (internal id), or null.</param>
/// <param name="IsActive">False for a deactivated account.</param>
/// <param name="HasPassword">True once a password-bound account has redeemed its activation link.</param>
/// <param name="IsLockedOut">True while failed sign-ins keep the account locked.</param>
/// <param name="IsBreakGlass">True for the break-glass administrator (configured, never created here).</param>
public sealed record AccountSummary(
    long UserId,
    Guid SubjectId,
    string UserName,
    string DisplayName,
    string? Email,
    string? LoginName,
    string? Upn,
    UserKind Kind,
    AccountBinding Binding,
    long? OrganizationId,
    bool IsActive,
    bool HasPassword,
    bool IsLockedOut,
    bool IsBreakGlass);

/// <summary>A new account. It never carries a password: a password-bound account gets an activation link.</summary>
/// <param name="UserName">The sign-in name (unique, compared normalized).</param>
/// <param name="DisplayName">The name shown in the shell and the lists.</param>
/// <param name="Email">The e-mail address (unique when given), or null.</param>
/// <param name="LoginName">The directory login name (unique when given), or null.</param>
/// <param name="Upn">The user principal name (unique when given), or null.</param>
/// <param name="Kind">Internal or external.</param>
/// <param name="Binding">The one sign-in mode the account accepts.</param>
/// <param name="OrganizationId">The organization (internal id), or null.</param>
public sealed record NewAccount(
    string UserName,
    string DisplayName,
    string? Email,
    string? LoginName,
    string? Upn,
    UserKind Kind,
    AccountBinding Binding,
    long? OrganizationId);

/// <summary>The editable fields of an account (the user name and the subject never change).</summary>
/// <param name="DisplayName">The name shown in the shell and the lists.</param>
/// <param name="Email">The e-mail address (unique when given), or null.</param>
/// <param name="LoginName">The directory login name (unique when given), or null.</param>
/// <param name="Upn">The user principal name (unique when given), or null.</param>
/// <param name="Kind">Internal or external.</param>
/// <param name="Binding">The one sign-in mode the account accepts.</param>
/// <param name="OrganizationId">The organization (internal id), or null.</param>
public sealed record AccountChanges(
    string DisplayName,
    string? Email,
    string? LoginName,
    string? Upn,
    UserKind Kind,
    AccountBinding Binding,
    long? OrganizationId);

/// <summary>A page of the account list. Every filter is optional (null matches all).</summary>
/// <param name="Search">Matched against the user name, display name and e-mail address, or null.</param>
/// <param name="OrganizationId">Only this organization's accounts, or null.</param>
/// <param name="Kind">Only this kind, or null.</param>
/// <param name="IsActive">Only active (true) or deactivated (false) accounts, or null.</param>
/// <param name="Page">The 1-based page number.</param>
/// <param name="PageSize">The page size (the implementation caps it).</param>
public sealed record AccountQuery(string? Search, long? OrganizationId, UserKind? Kind, bool? IsActive, int Page, int PageSize);

/// <summary>One page of accounts and the total number of matches.</summary>
/// <param name="Items">The accounts of the page, in a stable order.</param>
/// <param name="Total">The number of accounts matching the filters, on every page.</param>
public sealed record AccountPage(IReadOnlyList<AccountSummary> Items, int Total);

/// <summary>
/// A freshly issued activation ticket. <see cref="Ticket"/> is the sealed, single-use secret that goes into the
/// activation link shown to the administrator; it is never logged, stored in clear or put in an outbox payload.
/// </summary>
/// <param name="Ticket">The sealed ticket (link fragment only).</param>
/// <param name="TicketId">The ticket's public id, safe for logs and outbox payloads.</param>
/// <param name="ExpiresAt">When the ticket stops working.</param>
public sealed record ActivationIssue(string Ticket, string TicketId, DateTimeOffset ExpiresAt);

/// <summary>
/// Account administration for other modules (implemented by the Identity module, consumed by the Users module), so
/// the Users screens never reference Identity's tables or project. Every write records nothing but what the method
/// says; authorization is the caller's job.
/// </summary>
public interface IAccountAdministration
{
    /// <summary>Creates an account without a password; refuses a duplicate user name, e-mail, login name or UPN.</summary>
    Task<Outcome<AccountSummary>> CreateAsync(NewAccount account, CancellationToken cancellationToken);

    /// <summary>Changes the editable fields; refuses an unknown account and the same duplicates as create.</summary>
    Task<Outcome<AccountSummary>> UpdateAsync(long userId, AccountChanges changes, CancellationToken cancellationToken);

    /// <summary>
    /// Activates or deactivates the account. Deactivation bumps the user's session version, so the next API call of
    /// every open session answers 401.
    /// </summary>
    Task<Outcome<Done>> SetActiveAsync(long userId, bool isActive, CancellationToken cancellationToken);

    /// <summary>Ends a lockout and resets the failed sign-in counter.</summary>
    Task<Outcome<Done>> UnlockAsync(long userId, CancellationToken cancellationToken);

    /// <summary>The account, or null when none exists.</summary>
    Task<AccountSummary?> FindAsync(long userId, CancellationToken cancellationToken);

    /// <summary>One page of accounts matching the query.</summary>
    Task<AccountPage> ListAsync(AccountQuery query, CancellationToken cancellationToken);

    /// <summary>
    /// Issues a new activation ticket (the administrator's password reset): Password binding only. It clears any
    /// existing password and bumps the session version. Refused for the break-glass account and for Active Directory
    /// and SSO bindings.
    /// </summary>
    Task<Outcome<ActivationIssue>> IssueActivationAsync(long userId, CancellationToken cancellationToken);
}
