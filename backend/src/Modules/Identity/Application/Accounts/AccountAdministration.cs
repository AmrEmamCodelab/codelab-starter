using Starter.Application.Results;
using Starter.Application.Users;
using Starter.Modules.Identity.Application.Activation;
using Starter.Modules.Identity.Application.Auditing;
using Starter.Modules.Identity.Domain;

namespace Starter.Modules.Identity.Application.Accounts;

/// <summary>
/// The kernel's <see cref="IAccountAdministration"/> over the Identity accounts (consumed by the Users module).
/// Authorization is the caller's job. Accounts are created without a password; a password account then gets an
/// activation link (<see cref="IssueActivationAsync"/>). The break-glass administrator is configured, not administered:
/// it is refused for update, deactivation and activation, and only unlock applies to it.
/// </summary>
public sealed class AccountAdministration(
    IUserAccountRepository accounts,
    AccountWrites writes,
    AccountInputValidator validator,
    ActivationIssuer activation,
    IAuthEventSink audit) : IAccountAdministration
{
    /// <summary>The largest page <see cref="ListAsync"/> returns.</summary>
    public const int MaxPageSize = 100;

    /// <inheritdoc />
    public async Task<Outcome<AccountSummary>> CreateAsync(NewAccount account, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(account);
        var errors = validator.Validate(account);
        if (!errors.IsValid)
        {
            return Refusal.Invalid(errors);
        }

        var identifiers = new AccountIdentifiers(
            NormalizedUserName: UserNames.Normalize(account.UserName),
            NormalizedEmail: UserNames.NormalizeOptional(account.Email),
            NormalizedLoginName: UserNames.NormalizeOptional(account.LoginName),
            NormalizedUpn: UserNames.NormalizeOptional(account.Upn));
        if (await accounts.FindTakenIdentifierAsync(identifiers, exceptUserId: null, cancellationToken) is { } taken)
        {
            return Taken(taken);
        }

        var created = UserAccount.Register(new AccountRegistration(
            SubjectId: Guid.NewGuid(),
            UserName: account.UserName,
            Profile: AccountSummaries.ProfileOf(AccountSummaries.ChangesOf(account)),
            CreatedAt: writes.Now()));
        await writes.AddAsync(created, cancellationToken);
        return AccountSummaries.Of(created, writes.Now());
    }

    /// <inheritdoc />
    public async Task<Outcome<AccountSummary>> UpdateAsync(long userId, AccountChanges changes, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(changes);
        var errors = validator.Validate(changes);
        if (!errors.IsValid)
        {
            return Refusal.Invalid(errors);
        }

        var account = await accounts.FindByIdAsync(userId, cancellationToken);
        if (RefuseUnadministered(account) is { } refusal)
        {
            return refusal;
        }

        var identifiers = new AccountIdentifiers(
            NormalizedUserName: null,
            NormalizedEmail: UserNames.NormalizeOptional(changes.Email),
            NormalizedLoginName: UserNames.NormalizeOptional(changes.LoginName),
            NormalizedUpn: UserNames.NormalizeOptional(changes.Upn));
        if (await accounts.FindTakenIdentifierAsync(identifiers, exceptUserId: userId, cancellationToken) is { } taken)
        {
            return Taken(taken);
        }

        var credentialChanged = account!.ChangeProfile(AccountSummaries.ProfileOf(changes));
        await writes.SaveAsync(account, credentialChanged ? SessionImpact.EndSessions : SessionImpact.None, cancellationToken);
        return AccountSummaries.Of(account, writes.Now());
    }

    /// <inheritdoc />
    public async Task<Outcome<Done>> SetActiveAsync(long userId, bool isActive, CancellationToken cancellationToken)
    {
        var account = await accounts.FindByIdAsync(userId, cancellationToken);
        if (RefuseUnadministered(account) is { } refusal)
        {
            return refusal;
        }

        if (account!.IsActive == isActive)
        {
            return Done.Value;
        }

        if (isActive)
        {
            account.Reactivate();
            await writes.SaveAsync(account, SessionImpact.None, cancellationToken);
        }
        else
        {
            account.Deactivate();
            await writes.SaveAsync(account, SessionImpact.EndSessions, cancellationToken);
        }

        var eventType = isActive ? AuthEventType.AccountReactivated : AuthEventType.AccountDeactivated;
        await audit.RecordAsync(new AuthEventRecord(eventType, UserId: account.Id), cancellationToken);
        return Done.Value;
    }

    /// <inheritdoc />
    public async Task<Outcome<Done>> UnlockAsync(long userId, CancellationToken cancellationToken)
    {
        if (await accounts.FindByIdAsync(userId, cancellationToken) is not { } account)
        {
            return Refusal.NotFound(IdentityErrorCodes.AccountNotFound);
        }

        account.Unlock();
        await writes.SaveAsync(account, SessionImpact.None, cancellationToken);
        await audit.RecordAsync(new AuthEventRecord(AuthEventType.AccountUnlocked, UserId: account.Id), cancellationToken);
        return Done.Value;
    }

    /// <inheritdoc />
    public async Task<AccountSummary?> FindAsync(long userId, CancellationToken cancellationToken)
    {
        var account = await accounts.FindByIdAsync(userId, cancellationToken);
        return account is null ? null : AccountSummaries.Of(account, writes.Now());
    }

    /// <inheritdoc />
    public async Task<AccountPage> ListAsync(AccountQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        var pageSize = Math.Clamp(query.PageSize, 1, MaxPageSize);
        var page = Math.Max(query.Page, 1);
        var result = await accounts.ListAsync(
            new AccountListRequest(
                Search: UserNames.TrimOptional(query.Search),
                OrganizationId: query.OrganizationId,
                Kind: query.Kind,
                IsActive: query.IsActive,
                Skip: (page - 1) * pageSize,
                Take: pageSize),
            cancellationToken);
        var now = writes.Now();
        return new AccountPage([.. result.Items.Select(account => AccountSummaries.Of(account, now))], result.Total);
    }

    /// <inheritdoc />
    public async Task<Outcome<ActivationIssue>> IssueActivationAsync(long userId, CancellationToken cancellationToken)
    {
        var account = await accounts.FindByIdAsync(userId, cancellationToken);
        return account is null
            ? Refusal.NotFound(IdentityErrorCodes.AccountNotFound)
            : await activation.IssueAsync(account, cancellationToken);
    }

    private static Refusal Taken(AccountIdentifier identifier) => Refusal.Conflict(identifier switch
    {
        AccountIdentifier.UserName => IdentityErrorCodes.UserNameTaken,
        AccountIdentifier.Email => IdentityErrorCodes.EmailTaken,
        AccountIdentifier.LoginName => IdentityErrorCodes.LoginNameTaken,
        _ => IdentityErrorCodes.UpnTaken,
    });

    private static Refusal? RefuseUnadministered(UserAccount? account) => account switch
    {
        null => Refusal.NotFound(IdentityErrorCodes.AccountNotFound),
        { IsBreakGlass: true } => Refusal.Conflict(IdentityErrorCodes.BreakGlassProtected),
        _ => null,
    };
}
