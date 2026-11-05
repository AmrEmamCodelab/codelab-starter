using Starter.Application.Authorization;
using Starter.Modules.Identity.Domain;

namespace Starter.Modules.Identity.Application;

/// <summary>One account that configuration says must exist.</summary>
/// <param name="UserName">The user name.</param>
/// <param name="Password">The password (from the local secrets file or the environment, never a committed file).</param>
/// <param name="DisplayName">The display name.</param>
public sealed record SeedAccount(string UserName, string Password, string DisplayName)
{
    /// <summary>A log-safe description: the password is never printed.</summary>
    public override string ToString() => nameof(SeedAccount) + "(" + UserName + ")";
}

/// <summary>What the account seeding writes.</summary>
/// <param name="BreakGlass">The break-glass administrator (always).</param>
/// <param name="DevUsers">The Development users (empty outside Development).</param>
public sealed record AccountSeedPlan(SeedAccount BreakGlass, IReadOnlyList<SeedAccount> DevUsers);

/// <summary>
/// The permission keys a Development user is granted. They mirror keys the Access module declares; Identity never
/// references Access, and <see cref="IUserPermissionGrants"/> refuses a key the catalog does not hold, so a drift fails
/// the Development start loudly.
/// </summary>
public static class DevUserPermissionKeys
{
    /// <summary>The dashboard (Access <c>AccessPermissionKeys.DashboardView</c>).</summary>
    public const string DashboardView = "dashboard.view";

    /// <summary>Every key a Development user holds: the dashboard only (a low-privilege user).</summary>
    public static IReadOnlyCollection<string> All { get; } = [DashboardView];
}

/// <summary>
/// Makes the configured accounts exist (idempotent, every start of every node): creates a missing account, restores the
/// break-glass flag and replaces a hash whose configured password changed. The user's session version is bumped BEFORE
/// the new hash is saved, so a start that fails between the two leaves the old hash in place and the next start bumps
/// again (a spurious extra bump is harmless); bumping after the save could lose the bump for good and let sessions
/// opened with the old password live on. Development users get <see cref="DevUserPermissionKeys.All"/> through <see cref="IUserPermissionGrants"/>.
/// </summary>
public sealed class AccountSeeder(
    IUserAccountRepository accounts,
    IIdentityUnitOfWork unitOfWork,
    IPasswordHasher hasher,
    IUserPermissionGrants grants,
    TimeProvider clock)
{
    /// <summary>Writes <paramref name="plan"/>.</summary>
    /// <param name="plan">The accounts to seed.</param>
    /// <param name="sessionVersions">Bumped for each rotated account before its new hash is saved.</param>
    /// <param name="cancellationToken">Cancels the seeding.</param>
    /// <returns>The subjects whose password hash was replaced (their open sessions have already been ended).</returns>
    public async Task<IReadOnlyList<Guid>> SeedAsync(AccountSeedPlan plan, ISessionVersions sessionVersions, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentNullException.ThrowIfNull(sessionVersions);
        var rotated = new List<Guid>();
        var breakGlass = await EnsureAccountAsync(plan.BreakGlass, isBreakGlass: true, sessionVersions, cancellationToken);
        breakGlass.Account.RestoreBreakGlass();
        await unitOfWork.SaveChangesAsync(cancellationToken);
        Collect(breakGlass, rotated);
        foreach (var devUser in plan.DevUsers)
        {
            var seeded = await EnsureAccountAsync(devUser, isBreakGlass: false, sessionVersions, cancellationToken);
            await unitOfWork.SaveChangesAsync(cancellationToken);
            Collect(seeded, rotated);
            await grants.EnsureGrantedAsync(seeded.Account.Id, DevUserPermissionKeys.All, cancellationToken);
        }

        return rotated;
    }

    private static void Collect(SeededAccount seeded, List<Guid> rotated)
    {
        if (seeded.PasswordReplaced)
        {
            rotated.Add(seeded.Account.SubjectId);
        }
    }

    private async Task<SeededAccount> EnsureAccountAsync(
        SeedAccount seed, bool isBreakGlass, ISessionVersions sessionVersions, CancellationToken cancellationToken)
    {
        var existing = await accounts.FindByUserNameAsync(UserNames.Normalize(seed.UserName), cancellationToken);
        if (existing is null)
        {
            var created = UserAccount.Create(new NewUserAccount(
                SubjectId: Guid.NewGuid(),
                UserName: seed.UserName,
                DisplayName: seed.DisplayName,
                PasswordHash: hasher.Hash(seed.Password),
                IsBreakGlass: isBreakGlass,
                CreatedAt: clock.GetUtcNow()));
            accounts.Add(created);
            return new SeededAccount(created, PasswordReplaced: false);
        }

        if (hasher.Verify(existing.PasswordHash, seed.Password) != PasswordVerification.Failed)
        {
            return new SeededAccount(existing, PasswordReplaced: false);
        }

        await sessionVersions.BumpAsync(existing.SubjectId, cancellationToken);
        existing.ReplacePasswordHash(hasher.Hash(seed.Password));
        return new SeededAccount(existing, PasswordReplaced: true);
    }

    private sealed record SeededAccount(UserAccount Account, bool PasswordReplaced);
}
