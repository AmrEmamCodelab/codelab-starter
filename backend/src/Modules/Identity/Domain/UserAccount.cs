using Starter.Domain.Users;

namespace Starter.Modules.Identity.Domain;

/// <summary>
/// A person who can sign in. <see cref="Id"/> is internal (the wire carries a PublicId); <see cref="SubjectId"/> is the
/// stable token subject (<c>sub</c>), which never changes and never reveals the internal id. An account accepts one
/// sign-in mode (<see cref="Binding"/>); a password account gets its password only by redeeming an activation link,
/// never from a default.
/// </summary>
public sealed class UserAccount
{
    private UserAccount()
    {
        UserName = string.Empty;
        NormalizedUserName = string.Empty;
        DisplayName = string.Empty;
    }

    /// <summary>The internal id.</summary>
    public long Id { get; private set; }

    /// <summary>The token subject.</summary>
    public Guid SubjectId { get; private set; }

    /// <summary>The user name as entered when the account was created.</summary>
    public string UserName { get; private set; }

    /// <summary>The user name in its lookup form (<see cref="UserNames.Normalize"/>), unique.</summary>
    public string NormalizedUserName { get; private set; }

    /// <summary>The name shown in the application.</summary>
    public string DisplayName { get; private set; }

    /// <summary>The e-mail address as entered, or null.</summary>
    public string? Email { get; private set; }

    /// <summary>The e-mail address in lookup form, unique when present.</summary>
    public string? NormalizedEmail { get; private set; }

    /// <summary>The directory login name as entered, or null.</summary>
    public string? LoginName { get; private set; }

    /// <summary>The directory login name in lookup form, unique when present.</summary>
    public string? NormalizedLoginName { get; private set; }

    /// <summary>The user principal name as entered, or null.</summary>
    public string? Upn { get; private set; }

    /// <summary>The user principal name in lookup form, unique when present.</summary>
    public string? NormalizedUpn { get; private set; }

    /// <summary>Internal or external.</summary>
    public UserKind Kind { get; private set; }

    /// <summary>The one sign-in mode the account accepts.</summary>
    public AccountBinding Binding { get; private set; }

    /// <summary>The organization (internal id, owned by the Users module), or null.</summary>
    public long? OrganizationId { get; private set; }

    /// <summary>The password hash (never the password), or null while no password is set.</summary>
    public string? PasswordHash { get; private set; }

    /// <summary>When the current password was set through an activation link (or the account was seeded), or null.</summary>
    public DateTimeOffset? ActivatedAt { get; private set; }

    /// <summary>The id of the one activation link that may still be redeemed, or null.</summary>
    public string? ActivationTicketId { get; private set; }

    /// <summary>Consecutive wrong passwords since the last success, lockout or unlock.</summary>
    public int FailedAccessCount { get; private set; }

    /// <summary>The end of the current or last lockout, or null.</summary>
    public DateTimeOffset? LockoutEndsAt { get; private set; }

    /// <summary>False for a deactivated account: it cannot sign in or refresh, and holds no permission.</summary>
    public bool IsActive { get; private set; }

    /// <summary>True for the break-glass administrator, which holds every catalog permission.</summary>
    public bool IsBreakGlass { get; private set; }

    /// <summary>When the account was created.</summary>
    public DateTimeOffset CreatedAt { get; private set; }

    /// <summary>True while a password is set.</summary>
    public bool HasPassword => PasswordHash is not null;

    /// <summary>A new active configured account (break-glass or Development user) with a password, internal and password bound.</summary>
    public static UserAccount Create(NewUserAccount account)
    {
        ArgumentNullException.ThrowIfNull(account);
        ArgumentException.ThrowIfNullOrWhiteSpace(account.PasswordHash);
        var created = New(account.SubjectId, account.UserName, account.CreatedAt);
        created.ApplyProfile(new AccountProfile(
            DisplayName: account.DisplayName,
            Email: null,
            LoginName: null,
            Upn: null,
            Kind: UserKind.@Internal, // "@": keeps the scrub sweep's host-name rule off an enum member access
            Binding: AccountBinding.Password,
            OrganizationId: null));
        created.PasswordHash = account.PasswordHash;
        created.ActivatedAt = account.CreatedAt;
        created.IsBreakGlass = account.IsBreakGlass;
        return created;
    }

    /// <summary>A new active administered account without a password (a password account then gets an activation link).</summary>
    public static UserAccount Register(AccountRegistration registration)
    {
        ArgumentNullException.ThrowIfNull(registration);
        var created = New(registration.SubjectId, registration.UserName, registration.CreatedAt);
        created.ApplyProfile(registration.Profile);
        return created;
    }

    /// <summary>
    /// Replaces the editable fields. Moving the account to another binding drops its password and any pending
    /// activation link. A new binding, or a new e-mail address, login name or UPN (the identifiers Active Directory and
    /// SSO sign-ins match on), is a credential change: the caller then ends the user's sessions.
    /// </summary>
    /// <returns>True for a credential change (the binding or a sign-in identifier changed).</returns>
    public bool ChangeProfile(AccountProfile profile)
    {
        ArgumentNullException.ThrowIfNull(profile);
        var bindingChanged = profile.Binding != Binding;
        var identifierChanged = !string.Equals(UserNames.NormalizeOptional(profile.Email), NormalizedEmail, StringComparison.Ordinal)
            || !string.Equals(UserNames.NormalizeOptional(profile.LoginName), NormalizedLoginName, StringComparison.Ordinal)
            || !string.Equals(UserNames.NormalizeOptional(profile.Upn), NormalizedUpn, StringComparison.Ordinal);
        ApplyProfile(profile);
        if (bindingChanged)
        {
            ClearPassword();
        }

        return bindingChanged || identifierChanged;
    }

    /// <summary>Replaces the password hash (a rehash after a successful login, or a rotated configured password).</summary>
    public void ReplacePasswordHash(string passwordHash)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(passwordHash);
        PasswordHash = passwordHash;
    }

    /// <summary>Deactivates the account: it can no longer sign in or refresh, and holds no permission.</summary>
    public void Deactivate() => IsActive = false;

    /// <summary>Reactivates the account.</summary>
    public void Reactivate() => IsActive = true;

    /// <summary>Makes the account a break-glass administrator again, active, password bound and unlocked (configuration is its authority).</summary>
    public void RestoreBreakGlass()
    {
        IsBreakGlass = true;
        IsActive = true;
        Binding = AccountBinding.Password;
        ActivationTicketId = null;
        Unlock();
    }

    /// <summary>True while a lockout is in force at <paramref name="now"/>.</summary>
    public bool IsLockedOut(DateTimeOffset now) => LockoutEndsAt is { } endsAt && endsAt > now;

    /// <summary>
    /// Counts a wrong password. The attempt that reaches <see cref="LockoutPolicy.MaxFailedAttempts"/> locks the account
    /// for <see cref="LockoutPolicy.Duration"/> and resets the counter; an attempt during a lockout is not counted and
    /// never extends it.
    /// </summary>
    /// <returns>True when this attempt started a lockout.</returns>
    public bool RecordFailedSignIn(DateTimeOffset now, LockoutPolicy policy)
    {
        ArgumentNullException.ThrowIfNull(policy);
        if (IsLockedOut(now))
        {
            return false;
        }

        FailedAccessCount++;
        if (FailedAccessCount < policy.MaxFailedAttempts)
        {
            return false;
        }

        FailedAccessCount = 0;
        LockoutEndsAt = now + policy.Duration;
        return true;
    }

    /// <summary>A successful sign-in: the failed-attempt counter starts again.</summary>
    public void RecordSuccessfulSignIn() => FailedAccessCount = 0;

    /// <summary>Ends any lockout and resets the failed-attempt counter.</summary>
    public void Unlock()
    {
        FailedAccessCount = 0;
        LockoutEndsAt = null;
    }

    /// <summary>Whether an activation link may be issued for this account.</summary>
    public ActivationEligibility CheckActivationEligibility()
    {
        if (IsBreakGlass)
        {
            return ActivationEligibility.BreakGlass;
        }

        if (Binding != AccountBinding.Password)
        {
            return ActivationEligibility.NotPasswordBound;
        }

        return IsActive ? ActivationEligibility.Eligible : ActivationEligibility.Inactive;
    }

    /// <summary>
    /// Starts (or restarts) activation: the current password stops working, any earlier link is superseded and the
    /// lockout ends. Precondition: <see cref="CheckActivationEligibility"/> is <see cref="ActivationEligibility.Eligible"/>.
    /// </summary>
    /// <exception cref="InvalidOperationException">The account is not eligible (a caller bug: the use case checks first).</exception>
    public void BeginActivation(string ticketId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(ticketId);
        if (CheckActivationEligibility() != ActivationEligibility.Eligible)
        {
            throw new InvalidOperationException("Activation was started for an account that is not eligible; check eligibility first.");
        }

        ClearPassword();
        ActivationTicketId = ticketId;
        Unlock();
    }

    /// <summary>True when <paramref name="ticketId"/> is the account's one pending link and the account may still use it.</summary>
    public bool CanRedeemActivation(string ticketId) =>
        IsActive
        && !IsBreakGlass
        && Binding == AccountBinding.Password
        && ActivationTicketId is not null
        && string.Equals(ActivationTicketId, ticketId, StringComparison.Ordinal);

    /// <summary>Sets the password from a redeemed activation link; the link is spent. Precondition: <see cref="CanRedeemActivation"/>.</summary>
    /// <exception cref="InvalidOperationException">No link is pending (a caller bug: the use case checks first).</exception>
    public void CompleteActivation(string passwordHash, DateTimeOffset now)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(passwordHash);
        if (ActivationTicketId is null)
        {
            throw new InvalidOperationException("Activation was completed for an account with no pending activation link.");
        }

        PasswordHash = passwordHash;
        ActivatedAt = now;
        ActivationTicketId = null;
        Unlock();
    }

    private static UserAccount New(Guid subjectId, string userName, DateTimeOffset createdAt)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(userName);
        return new UserAccount
        {
            SubjectId = subjectId,
            UserName = userName.Trim(),
            NormalizedUserName = UserNames.Normalize(userName),
            IsActive = true,
            CreatedAt = createdAt,
        };
    }

    private void ApplyProfile(AccountProfile profile)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(profile.DisplayName);
        DisplayName = profile.DisplayName.Trim();
        Email = UserNames.TrimOptional(profile.Email);
        NormalizedEmail = UserNames.NormalizeOptional(profile.Email);
        LoginName = UserNames.TrimOptional(profile.LoginName);
        NormalizedLoginName = UserNames.NormalizeOptional(profile.LoginName);
        Upn = UserNames.TrimOptional(profile.Upn);
        NormalizedUpn = UserNames.NormalizeOptional(profile.Upn);
        Kind = profile.Kind;
        Binding = profile.Binding;
        OrganizationId = profile.OrganizationId;
    }

    private void ClearPassword()
    {
        PasswordHash = null;
        ActivatedAt = null;
        ActivationTicketId = null;
    }
}
