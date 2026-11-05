using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using Codelab.Security.Keys;
using Codelab.Security.Tickets;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Starter.Application.Users;
using Starter.Domain.Users;
using Starter.Integration.Tests.Support;
using Starter.Modules.Identity.Infrastructure.Options;
using Starter.Tests.Hosting;

namespace Starter.Integration.Tests.Accounts;

/// <summary>
/// The Development host for the account security flows, with three test-only overrides: sealed tickets switched on
/// with a fresh random key ring (activation links), a lockout of <see cref="MaxFailedAttempts"/> attempts, and a clock
/// a test can shift forward for one call (<see cref="Clock"/>) to pass a lockout window without waiting.
/// </summary>
public sealed class AccountsHost() : IntegrationHost(Environments.Development)
{
    /// <summary>The lockout threshold the host runs with.</summary>
    public const int MaxFailedAttempts = 3;

    /// <summary>The lockout length the host runs with, in minutes.</summary>
    public const int LockoutMinutes = 5;

    private const string ActivatePath = "/api/auth/activate";
    private const int PasswordRandomBytes = 18;
    private const string PasswordSuffix = "Aa1!";
    private const string UserNamePrefix = "it-acct-";
    private const int UserNameRandomBytes = 4;
    private const int AccountOrganizationId = 1;

    /// <summary>The host's clock (system time plus an offset the tests move forward).</summary>
    public ShiftableClock Clock { get; } = new();

    /// <summary>A fresh random password that meets the activation password rule.</summary>
    public static string NewPassword() => Convert.ToBase64String(RandomNumberGenerator.GetBytes(PasswordRandomBytes)) + PasswordSuffix;

    /// <summary>Runs <paramref name="action"/> against the host's account administration (as the Users module would).</summary>
    public async Task<T> WithAdministrationAsync<T>(Func<IAccountAdministration, Task<T>> action)
    {
        ArgumentNullException.ThrowIfNull(action);
        await using var scope = Factory.Services.CreateAsyncScope();
        return await action(scope.ServiceProvider.GetRequiredService<IAccountAdministration>());
    }

    /// <summary>Creates a password account, issues its activation link and redeems it with a fresh password.</summary>
    public async Task<ActivatedUser> CreateActivatedUserAsync()
    {
        var userName = UserNamePrefix + Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(UserNameRandomBytes));
        var created = await WithAdministrationAsync(admin => admin.CreateAsync(
            new NewAccount(
                UserName: userName,
                DisplayName: userName,
                Email: null,
                LoginName: null,
                Upn: null,
                Kind: UserKind.External,
                Binding: AccountBinding.Password,
                OrganizationId: AccountOrganizationId),
            CancellationToken.None));
        var issued = await WithAdministrationAsync(admin => admin.IssueActivationAsync(created.Value.UserId, CancellationToken.None));
        var chosen = NewPassword();
        using var http = Factory.CreateClient();
        using var response = await PostActivateAsync(http, issued.Value.Ticket, chosen);
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        return new ActivatedUser(created.Value.UserId, userName, chosen);
    }

    /// <summary>Posts an activation; the response is returned as is.</summary>
    public static Task<HttpResponseMessage> PostActivateAsync(HttpClient http, string ticket, string password)
    {
        ArgumentNullException.ThrowIfNull(http);
        return http.PostAsJsonAsync(ActivatePath, new { ticket, password });
    }

    /// <inheritdoc />
    protected override void ConfigureServices(IServiceCollection services)
    {
        base.ConfigureServices(services);
        services.AddSingleton<TimeProvider>(Clock);
        services.PostConfigure<LockoutOptions>(options =>
        {
            options.MaxFailedAttempts = MaxFailedAttempts;
            options.LockoutMinutes = LockoutMinutes;
        });
        var keyId = StarterTestAccounts.NewKeyId();
        services.PostConfigure<SealedTicketOptions>(options =>
        {
            options.Enabled = true;
            options.ActiveKeyId = keyId;
            options.Keys.Clear();
            options.Keys.Add(new KeyEntryOptions { KeyId = keyId, MaterialBase64 = StarterTestAccounts.NewKeyBase64() });
        });
    }
}

/// <summary>An activated password account.</summary>
/// <param name="UserId">The internal id.</param>
/// <param name="UserName">The user name.</param>
/// <param name="Password">The password set through the activation link.</param>
public sealed record ActivatedUser(long UserId, string UserName, string Password);

/// <summary>
/// System time plus an offset. Shift it only around calls that issue (never validate) tokens and reset it right after:
/// the bearer token lifetime check reads the system clock, so tokens issued in the shifted future are not valid yet.
/// </summary>
public sealed class ShiftableClock : TimeProvider
{
    private long _offsetTicks;

    /// <inheritdoc />
    public override DateTimeOffset GetUtcNow() => System.GetUtcNow() + TimeSpan.FromTicks(Interlocked.Read(ref _offsetTicks));

    /// <summary>Moves the clock forward by <paramref name="delta"/> until the returned scope is disposed.</summary>
    public IDisposable Advance(TimeSpan delta)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(delta, TimeSpan.Zero);
        Interlocked.Add(ref _offsetTicks, delta.Ticks);
        return new Shift(this, delta);
    }

    private sealed class Shift(ShiftableClock clock, TimeSpan delta) : IDisposable
    {
        public void Dispose() => Interlocked.Add(ref clock._offsetTicks, -delta.Ticks);
    }
}

/// <summary>Tests sharing the accounts host (sequential, so a clock shift never overlaps another test).</summary>
[CollectionDefinition(Name)]
public sealed class AccountsHostGroup : ICollectionFixture<AccountsHost>
{
    /// <summary>The collection name.</summary>
    public const string Name = "accounts host";
}
