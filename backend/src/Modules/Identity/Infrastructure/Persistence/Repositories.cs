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
    public void Add(UserAccount account) => context.Accounts.Add(account);
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
