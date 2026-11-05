using Starter.Modules.Identity.Domain;

namespace Starter.Modules.Identity.Application.Accounts;

/// <summary>What saving an account change does to the user's open sessions.</summary>
public enum SessionImpact
{
    /// <summary>Open sessions keep working (a profile edit, an unlock, a reactivation).</summary>
    None = 1,

    /// <summary>
    /// Every open session of the user ends: the session version is bumped, so the next API call of each answers 401 and
    /// its next refresh is refused (deactivation; password reset; activation; binding change).
    /// </summary>
    EndSessions = 2,
}

/// <summary>
/// The write side of account administration: the clock, password hashing, staging a new account and saving a change.
/// It owns the ordering rule for credential changes: the session version is bumped BEFORE the change is saved, so a
/// save that fails afterwards leaves at worst a spurious bump (harmless), never a saved change whose old sessions live on.
/// </summary>
public sealed class AccountWrites(
    IUserAccountRepository accounts,
    IIdentityUnitOfWork unitOfWork,
    ISessionVersions versions,
    IPasswordHasher hasher,
    TimeProvider clock)
{
    /// <summary>The current time.</summary>
    public DateTimeOffset Now() => clock.GetUtcNow();

    /// <summary>A new salted hash of <paramref name="password"/>.</summary>
    public string HashPassword(string password) => hasher.Hash(password);

    /// <summary>Stages <paramref name="account"/> and saves it (its id is assigned).</summary>
    public async Task AddAsync(UserAccount account, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(account);
        accounts.Add(account);
        await unitOfWork.SaveChangesAsync(cancellationToken);
    }

    /// <summary>Saves the pending change to <paramref name="account"/>, ending the user's sessions first when <paramref name="impact"/> says so.</summary>
    public async Task SaveAsync(UserAccount account, SessionImpact impact, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(account);
        if (impact == SessionImpact.EndSessions)
        {
            await versions.BumpAsync(account.SubjectId, cancellationToken);
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);
    }
}
