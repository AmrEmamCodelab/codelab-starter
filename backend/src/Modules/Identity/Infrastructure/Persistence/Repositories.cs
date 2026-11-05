using Microsoft.EntityFrameworkCore;
using Starter.Modules.Identity.Application;
using Starter.Modules.Identity.Domain;

namespace Starter.Modules.Identity.Infrastructure.Persistence;

/// <summary><see cref="IUserAccountRepository"/> over <see cref="IdentityDbContext"/>.</summary>
public sealed class UserAccountRepository(IdentityDbContext context) : IUserAccountRepository
{
    /// <inheritdoc />
    public Task<UserAccount?> FindByUserNameAsync(string userName, CancellationToken cancellationToken)
    {
        var normalized = UserNames.Normalize(userName);
        return context.Accounts.SingleOrDefaultAsync(account => account.NormalizedUserName == normalized, cancellationToken);
    }

    /// <inheritdoc />
    public Task<UserAccount?> FindByIdAsync(long userId, CancellationToken cancellationToken) =>
        context.Accounts.SingleOrDefaultAsync(account => account.Id == userId, cancellationToken);

    /// <inheritdoc />
    public Task<UserAccount?> FindBySubjectAsync(Guid subjectId, CancellationToken cancellationToken) =>
        context.Accounts.SingleOrDefaultAsync(account => account.SubjectId == subjectId, cancellationToken);

    /// <inheritdoc />
    public Task<UserAccount?> FindByMatchAsync(AccountMatchField field, string value, CancellationToken cancellationToken)
    {
        if (UserNames.NormalizeOptional(value) is not { } normalized)
        {
            return Task.FromResult<UserAccount?>(null);
        }

        var query = field switch
        {
            AccountMatchField.Email => context.Accounts.Where(account => account.NormalizedEmail == normalized),
            AccountMatchField.LoginName => context.Accounts.Where(account => account.NormalizedLoginName == normalized),
            AccountMatchField.Upn => context.Accounts.Where(account => account.NormalizedUpn == normalized),
            _ => throw new ArgumentOutOfRangeException(nameof(field), field, "Unknown account match field."),
        };
        return query.SingleOrDefaultAsync(cancellationToken);
    }

    /// <inheritdoc />
    public async Task<AccountIdentifier?> FindTakenIdentifierAsync(
        AccountIdentifiers identifiers, long? exceptUserId, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(identifiers);
        var others = context.Accounts.Where(account => exceptUserId == null || account.Id != exceptUserId);
        if (identifiers.NormalizedUserName is { } userName && await others.AnyAsync(account => account.NormalizedUserName == userName, cancellationToken))
        {
            return AccountIdentifier.UserName;
        }

        if (identifiers.NormalizedEmail is { } email && await others.AnyAsync(account => account.NormalizedEmail == email, cancellationToken))
        {
            return AccountIdentifier.Email;
        }

        if (identifiers.NormalizedLoginName is { } loginName && await others.AnyAsync(account => account.NormalizedLoginName == loginName, cancellationToken))
        {
            return AccountIdentifier.LoginName;
        }

        return identifiers.NormalizedUpn is { } upn && await others.AnyAsync(account => account.NormalizedUpn == upn, cancellationToken)
            ? AccountIdentifier.Upn
            : null;
    }

    /// <inheritdoc />
    public async Task<AccountListPage> ListAsync(AccountListRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var query = Filter(context.Accounts.AsNoTracking(), request);
        var total = await query.CountAsync(cancellationToken);
        var items = await query
            .OrderBy(account => account.NormalizedUserName)
            .ThenBy(account => account.Id)
            .Skip(request.Skip)
            .Take(request.Take)
            .ToListAsync(cancellationToken);
        return new AccountListPage(items, total);
    }

    /// <inheritdoc />
    public void Add(UserAccount account) => context.Accounts.Add(account);

    private static IQueryable<UserAccount> Filter(IQueryable<UserAccount> query, AccountListRequest request)
    {
        if (request.Search is { } search)
        {
            var normalized = UserNames.Normalize(search);
            query = query.Where(account => account.NormalizedUserName.Contains(normalized)
                || account.DisplayName.Contains(search)
                || (account.NormalizedEmail != null && account.NormalizedEmail.Contains(normalized)));
        }

        if (request.OrganizationId is { } organizationId)
        {
            query = query.Where(account => account.OrganizationId == organizationId);
        }

        if (request.Kind is { } kind)
        {
            query = query.Where(account => account.Kind == kind);
        }

        return request.IsActive is { } isActive ? query.Where(account => account.IsActive == isActive) : query;
    }
}

/// <summary><see cref="IUserSessionRepository"/> over <see cref="IdentityDbContext"/>.</summary>
public sealed class UserSessionRepository(IdentityDbContext context) : IUserSessionRepository
{
    /// <inheritdoc />
    public Task<UserSession?> FindBySessionIdAsync(string sessionId, CancellationToken cancellationToken) =>
        context.Sessions.SingleOrDefaultAsync(session => session.SessionId == sessionId, cancellationToken);

    /// <inheritdoc />
    public void Add(UserSession session) => context.Sessions.Add(session);
}
