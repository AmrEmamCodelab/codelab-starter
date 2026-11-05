using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Starter.Infrastructure.Startup;
using Starter.Modules.Identity.Application;
using Starter.Modules.Identity.Infrastructure.Options;

namespace Starter.Modules.Identity.Infrastructure.Startup;

/// <summary>
/// The Accounts-phase startup task: seeds the break-glass administrator from <c>Identity:BreakGlass</c> (the host does
/// not start without its password) and, in Development ONLY, the <c>Identity:DevUsers</c>. An account whose configured
/// password changed gets its session version bumped (by <see cref="AccountSeeder"/>, before the new hash is saved), so every session opened with the old (possibly leaked) password
/// fails its next refresh and its access tokens are refused.
/// </summary>
public sealed class AccountSeedingTask(
    AccountSeeder seeder,
    ISessionVersions sessionVersions,
    IOptions<BreakGlassOptions> breakGlass,
    IOptions<DevUsersOptions> devUsers,
    IHostEnvironment environment) : IStartupTask
{
    /// <inheritdoc />
    public StartupPhase Phase => StartupPhase.Accounts;

    /// <inheritdoc />
    public Task RunAsync(CancellationToken cancellationToken) => seeder.SeedAsync(BuildPlan(), sessionVersions, cancellationToken);

    /// <summary>The accounts to seed in this environment.</summary>
    /// <exception cref="InvalidOperationException">No break-glass password is configured.</exception>
    public AccountSeedPlan BuildPlan()
    {
        var admin = breakGlass.Value;
        if (string.IsNullOrWhiteSpace(admin.Password))
        {
            throw new InvalidOperationException(
                $"{BreakGlassOptions.SectionName}:{nameof(BreakGlassOptions.Password)} is not configured. Run tools/devseed/New-LocalSecrets.ps1 (Development) or set it in the environment.");
        }

        var adminAccount = new SeedAccount(UserName: admin.UserName, Password: admin.Password, DisplayName: admin.DisplayName);
        return new AccountSeedPlan(BreakGlass: adminAccount, DevUsers: environment.IsDevelopment() ? DevAccounts() : []);
    }

    private SeedAccount[] DevAccounts() =>
    [
        .. devUsers.Value.Users.Select(user => new SeedAccount(
            UserName: user.UserName!,
            Password: user.Password!,
            DisplayName: string.IsNullOrWhiteSpace(user.DisplayName) ? user.UserName! : user.DisplayName)),
    ];
}
