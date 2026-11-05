using Starter.Modules.Identity.Domain;

namespace Starter.Modules.Identity.Application.LoginModes;

/// <summary>The account a basic sign-in may check, as <see cref="BasicLoginAccounts"/> found it.</summary>
/// <param name="Account">The account to check, or null (no such user, or one the closed mode excludes).</param>
/// <param name="ExcludedUserId">The id of an account that exists but is excluded because basic is off, or null.</param>
/// <param name="IsModeOpen">True when <c>Auth:Modes</c> lists basic.</param>
public sealed record BasicLoginTarget(UserAccount? Account, long? ExcludedUserId, bool IsModeOpen);

/// <summary>
/// Which accounts basic login (<c>POST /api/auth/login</c>) may sign in. While <c>Auth:Modes</c> lists basic, every
/// password account. While it does not, only the break-glass administrator: the emergency account must work exactly when
/// the directory or the identity provider is down, so an Active Directory-only or SSO-only deployment keeps one
/// password path, for that account alone. Every other account is excluded before its password is checked, so a closed
/// mode neither counts toward an ordinary account's lockout nor tells accounts apart.
/// </summary>
public sealed class BasicLoginAccounts(IUserAccountRepository accounts, ILoginModes modes)
{
    /// <summary>True when <c>Auth:Modes</c> lists basic.</summary>
    public bool IsModeOpen => modes.IsEnabled(SignInMode.Basic);

    /// <summary>The account with <paramref name="normalizedUserName"/> (<see cref="UserNames.Normalize"/>), filtered by the mode.</summary>
    public async Task<BasicLoginTarget> FindAsync(string normalizedUserName, CancellationToken cancellationToken)
    {
        var account = await accounts.FindByUserNameAsync(normalizedUserName, cancellationToken);
        var open = IsModeOpen;
        if (open || account is null || account.IsBreakGlass)
        {
            return new BasicLoginTarget(account, ExcludedUserId: null, IsModeOpen: open);
        }

        return new BasicLoginTarget(Account: null, ExcludedUserId: account.Id, IsModeOpen: open);
    }
}
