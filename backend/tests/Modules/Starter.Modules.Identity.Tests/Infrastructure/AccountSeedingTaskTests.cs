using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Starter.Infrastructure.Startup;
using Starter.Modules.Identity.Application;
using Starter.Modules.Identity.Infrastructure.Options;
using Starter.Modules.Identity.Infrastructure.Startup;
using Starter.Modules.Identity.Tests.Fakes;
using OptionsFactory = Microsoft.Extensions.Options.Options;

namespace Starter.Modules.Identity.Tests.Infrastructure;

public sealed class AccountSeedingTaskTests
{
    private static readonly string AdminSecret = Guid.NewGuid().ToString("N");
    private static readonly string DevSecret = Guid.NewGuid().ToString("N");
    private readonly IdentityHarness _harness = new();
    private readonly FakePermissionGrants _grants = new();

    private InMemoryIdentityStore Store => _harness.Store;

    [Fact]
    public async Task Outside_Development_only_the_break_glass_account_is_seeded()
    {
        await Task(Environments.Production).RunAsync(CancellationToken.None);

        var admin = Assert.Single(Store.Accounts);
        Assert.Equal("root", admin.UserName);
        Assert.True(admin.IsBreakGlass);
        Assert.Equal(PlainTextHasher.HashOf(AdminSecret), admin.PasswordHash);
        Assert.Empty(_grants.Calls);
    }

    [Theory]
    [InlineData("Staging")]
    [InlineData("Production")]
    public void The_plan_has_no_dev_users_outside_Development(string environment) =>
        Assert.Empty(Task(environment).BuildPlan().DevUsers);

    [Fact]
    public async Task In_Development_the_dev_users_are_seeded_and_granted_the_dashboard_only()
    {
        await Task(Environments.Development).RunAsync(CancellationToken.None);

        var dev = Assert.Single(Store.Accounts, account => !account.IsBreakGlass);
        Assert.Equal("dev", dev.UserName);
        Assert.Equal("dev", dev.DisplayName);
        var call = Assert.Single(_grants.Calls);
        Assert.Equal(dev.Id, call.UserId);
        Assert.Equal([DevUserPermissionKeys.DashboardView], call.Keys);
        Assert.Equal("dashboard.view", DevUserPermissionKeys.DashboardView);
    }

    [Fact]
    public async Task Seeding_is_idempotent_and_applies_a_changed_password_and_the_break_glass_flag()
    {
        await Task(Environments.Development).RunAsync(CancellationToken.None);
        var admin = Store.Accounts.Single(account => account.IsBreakGlass);
        admin.Deactivate();

        var rotated = Guid.NewGuid().ToString("N");
        await Task(Environments.Development, rotated).RunAsync(CancellationToken.None);

        Assert.Equal(2, Store.Accounts.Count);
        Assert.True(admin.IsActive);
        Assert.Equal(PlainTextHasher.HashOf(rotated), admin.PasswordHash);
        Assert.Equal(2, _grants.Calls.Count);
    }

    [Fact]
    public async Task An_unchanged_password_leaves_the_session_version_alone()
    {
        await Task(Environments.Production).RunAsync(CancellationToken.None);
        await Task(Environments.Production).RunAsync(CancellationToken.None);

        Assert.Empty(_harness.Versions.Versions);
    }

    [Fact]
    public async Task A_rotated_break_glass_password_ends_the_sessions_opened_with_the_old_one()
    {
        await Task(Environments.Production).RunAsync(CancellationToken.None);
        var signedIn = await _harness.SignInAsync("root", AdminSecret);

        await Task(Environments.Production, Guid.NewGuid().ToString("N")).RunAsync(CancellationToken.None);
        var outcome = await _harness.Refresh().ExecuteAsync(new RefreshCommand(signedIn.RefreshToken), CancellationToken.None);

        Assert.Equal(1, _harness.Versions.Versions[Store.Accounts.Single().SubjectId]);
        Assert.Equal(IdentityErrorCodes.RefreshRejected, outcome.Refusal?.Code);
    }

    [Fact]
    public async Task A_start_that_fails_while_ending_the_old_sessions_still_ends_them_on_the_next_start()
    {
        await Task(Environments.Production).RunAsync(CancellationToken.None);
        var signedIn = await _harness.SignInAsync("root", AdminSecret);
        var newSecret = Guid.NewGuid().ToString("N");

        await Assert.ThrowsAsync<TimeoutException>(() =>
            Task(Environments.Production, newSecret, new FailingSessionVersions()).RunAsync(CancellationToken.None));
        Assert.Equal(PlainTextHasher.HashOf(AdminSecret), Store.Accounts.Single().PasswordHash);

        await Task(Environments.Production, newSecret).RunAsync(CancellationToken.None);
        var outcome = await _harness.Refresh().ExecuteAsync(new RefreshCommand(signedIn.RefreshToken), CancellationToken.None);

        Assert.Equal(PlainTextHasher.HashOf(newSecret), Store.Accounts.Single().PasswordHash);
        Assert.Equal(IdentityErrorCodes.RefreshRejected, outcome.Refusal?.Code);
    }

    [Fact]
    public async Task The_seeder_reports_the_subjects_whose_password_it_replaced()
    {
        var seeder = new AccountSeeder(Store, Store, new PlainTextHasher(), _grants, _harness.Clock);
        Assert.Empty(await seeder.SeedAsync(Plan(AdminSecret), _harness.Versions, CancellationToken.None));
        Assert.Empty(await seeder.SeedAsync(Plan(AdminSecret), _harness.Versions, CancellationToken.None));

        var rotated = await seeder.SeedAsync(Plan(Guid.NewGuid().ToString("N")), _harness.Versions, CancellationToken.None);

        Assert.Equal([Store.Accounts.Single().SubjectId], rotated);
    }

    [Fact]
    public async Task A_missing_break_glass_password_stops_the_start_with_a_clear_message()
    {
        var error = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            Task(Environments.Production, adminSecret: null).RunAsync(CancellationToken.None));

        Assert.Contains("Identity:BreakGlass:Password", error.Message, StringComparison.Ordinal);
        Assert.Empty(Store.Accounts);
    }

    [Fact]
    public void The_task_runs_in_the_accounts_phase() =>
        Assert.Equal(StartupPhase.Accounts, Task(Environments.Production).Phase);

    private static AccountSeedPlan Plan(string adminSecret) => new(new SeedAccount("root", adminSecret, "root"), DevUsers: []);

    private AccountSeedingTask Task(string environment) => Task(environment, AdminSecret);

    private AccountSeedingTask Task(string environment, string? adminSecret) => Task(environment, adminSecret, _harness.Versions);

    private AccountSeedingTask Task(string environment, string? adminSecret, ISessionVersions versions)
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            [$"{BreakGlassOptions.SectionName}:{nameof(BreakGlassOptions.UserName)}"] = "root",
            [$"{BreakGlassOptions.SectionName}:{nameof(BreakGlassOptions.Password)}"] = adminSecret,
            [$"{DevUsersOptions.SectionName}:0:{nameof(DevUserOptions.UserName)}"] = "dev",
            [$"{DevUsersOptions.SectionName}:0:{nameof(DevUserOptions.Password)}"] = DevSecret,
        }).Build();
        var breakGlass = new BreakGlassOptions();
        configuration.GetSection(BreakGlassOptions.SectionName).Bind(breakGlass);
        var devUsers = new DevUsersOptions();
        configuration.GetSection(DevUsersOptions.SectionName).Bind(devUsers.Users);
        var seeder = new AccountSeeder(Store, Store, new PlainTextHasher(), _grants, new Microsoft.Extensions.Time.Testing.FakeTimeProvider(TestClock.Start));
        return new AccountSeedingTask(seeder, versions, OptionsFactory.Create(breakGlass), OptionsFactory.Create(devUsers), new TestEnvironment(environment));
    }

    /// <summary>A session-version store that is down (a transient SQL error or a host kill mid-bump).</summary>
    private sealed class FailingSessionVersions : ISessionVersions
    {
        public Task<long> CurrentAsync(Guid subjectId, CancellationToken cancellationToken) => throw new TimeoutException();

        public Task<long> BumpAsync(Guid subjectId, CancellationToken cancellationToken) => throw new TimeoutException();
    }

    private sealed class TestEnvironment(string name) : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = name;

        public string ApplicationName { get; set; } = "Starter.Modules.Identity.Tests";

        public string ContentRootPath { get; set; } = AppContext.BaseDirectory;

        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
}
