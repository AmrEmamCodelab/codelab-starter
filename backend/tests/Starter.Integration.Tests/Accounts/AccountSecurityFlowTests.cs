using System.Net;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Starter.Application.Users;
using Starter.Domain.Users;
using Starter.Integration.Tests.Support;
using Starter.Modules.Identity.Domain;
using Starter.Modules.Identity.Infrastructure.Persistence;
using Starter.Tests.Hosting;

namespace Starter.Integration.Tests.Accounts;

/// <summary>
/// The phase 3 account security exit criteria end to end on a real database: activation then login, lockout after N
/// failures (the right password refused while locked, accepted after the window), refresh rotation and reuse, a
/// deactivated user's next call refused, and logout ending only its own session. The auth event trail is checked in
/// the database.
/// </summary>
[Collection(AccountsHostGroup.Name)]
public sealed class AccountSecurityFlowTests(AccountsHost host)
{
    private const string InvalidCredentialsCode = "IDENTITY_INVALID_CREDENTIALS";
    private const string RefreshRejectedCode = "IDENTITY_REFRESH_REJECTED";
    private const string ActivationRejectedCode = "IDENTITY_ACTIVATION_REJECTED";

    [Fact]
    public async Task AnActivatedUserLogsInAndTheLinkCannotBeUsedTwice()
    {
        var user = await host.CreateActivatedUserAsync();
        var issued = await host.WithAdministrationAsync(admin => admin.FindAsync(user.UserId, CancellationToken.None));
        using var client = host.CreateClient();

        var session = await client.LoginAsync(user.UserName, user.Password);
        using var me = await client.GetAsync(StarterClient.Paths.MyPermissions, session.AccessToken);

        Assert.Equal(HttpStatusCode.OK, me.StatusCode);
        Assert.True(issued!.HasPassword);
        Assert.Contains(AuthEventType.AccountActivated, await EventTypesAsync(user.UserId));
    }

    [Fact]
    public async Task AReplayedOrSupersededActivationLinkIsRejected()
    {
        var created = await host.WithAdministrationAsync(admin => admin.CreateAsync(
            new NewAccount(
                UserName: "it-link-" + Guid.NewGuid().ToString("N"),
                DisplayName: "Link",
                Email: null,
                LoginName: null,
                Upn: null,
                Kind: UserKind.External,
                Binding: AccountBinding.Password,
                OrganizationId: null),
            CancellationToken.None));
        var first = await host.WithAdministrationAsync(admin => admin.IssueActivationAsync(created.Value.UserId, CancellationToken.None));
        var second = await host.WithAdministrationAsync(admin => admin.IssueActivationAsync(created.Value.UserId, CancellationToken.None));
        using var http = host.Factory.CreateClient();

        using var superseded = await AccountsHost.PostActivateAsync(http, first.Value.Ticket, AccountsHost.NewPassword());
        using var redeemed = await AccountsHost.PostActivateAsync(http, second.Value.Ticket, AccountsHost.NewPassword());
        using var replayed = await AccountsHost.PostActivateAsync(http, second.Value.Ticket, AccountsHost.NewPassword());

        Assert.Equal(ActivationRejectedCode, await StarterProblems.AssertProblemAsync(superseded, HttpStatusCode.BadRequest));
        Assert.Equal(HttpStatusCode.NoContent, redeemed.StatusCode);
        Assert.Equal(ActivationRejectedCode, await StarterProblems.AssertProblemAsync(replayed, HttpStatusCode.BadRequest));
    }

    [Fact]
    public async Task LockoutRefusesTheRightPasswordUntilTheWindowEnds()
    {
        var user = await host.CreateActivatedUserAsync();
        using var client = host.CreateClient();

        for (var attempt = 0; attempt < AccountsHost.MaxFailedAttempts; attempt++)
        {
            using var wrong = await client.PostLoginAsync(user.UserName, AccountsHost.NewPassword());
            Assert.Equal(InvalidCredentialsCode, await StarterProblems.AssertProblemAsync(wrong, HttpStatusCode.BadRequest));
        }

        using var whileLocked = await client.PostLoginAsync(user.UserName, user.Password);
        Assert.Equal(InvalidCredentialsCode, await StarterProblems.AssertProblemAsync(whileLocked, HttpStatusCode.BadRequest));
        Assert.True((await host.WithAdministrationAsync(admin => admin.FindAsync(user.UserId, CancellationToken.None)))!.IsLockedOut);

        SessionTokens afterWindow;
        using (host.Clock.Advance(TimeSpan.FromMinutes(AccountsHost.LockoutMinutes)))
        {
            afterWindow = await client.LoginAsync(user.UserName, user.Password);
        }

        Assert.False(string.IsNullOrEmpty(afterWindow.AccessToken));
        Assert.Contains(AuthEventType.AccountLockedOut, await EventTypesAsync(user.UserId));
    }

    [Fact]
    public async Task RefreshRotatesAndAReusedTokenEndsTheSession()
    {
        var user = await host.CreateActivatedUserAsync();
        using var client = host.CreateClient();
        var first = await client.LoginAsync(user.UserName, user.Password);

        using var rotated = await client.PostRefreshAsync(first.RefreshToken);
        var second = await StarterClient.ReadSessionAsync(rotated);
        using var reused = await client.PostRefreshAsync(first.RefreshToken);
        using var afterTheft = await client.PostRefreshAsync(second.RefreshToken);

        Assert.NotEqual(first.RefreshToken, second.RefreshToken);
        Assert.Equal(RefreshRejectedCode, await StarterProblems.AssertProblemAsync(reused, HttpStatusCode.BadRequest));
        Assert.Equal(RefreshRejectedCode, await StarterProblems.AssertProblemAsync(afterTheft, HttpStatusCode.BadRequest));
        Assert.Contains(AuthEventType.RefreshRejected, await EventTypesAsync(user.UserId));
    }

    [Fact]
    public async Task ADeactivatedUsersNextCallGets401()
    {
        var user = await host.CreateActivatedUserAsync();
        using var client = host.CreateClient();
        var session = await client.LoginAsync(user.UserName, user.Password);
        using var before = await client.GetAsync(StarterClient.Paths.MyPermissions, session.AccessToken);

        var deactivated = await host.WithAdministrationAsync(admin => admin.SetActiveAsync(user.UserId, isActive: false, CancellationToken.None));
        using var after = await client.GetAsync(StarterClient.Paths.MyPermissions, session.AccessToken);
        using var refresh = await client.PostRefreshAsync(session.RefreshToken);
        using var login = await client.PostLoginAsync(user.UserName, user.Password);

        Assert.Equal(HttpStatusCode.OK, before.StatusCode);
        Assert.True(deactivated.IsSuccess);
        await StarterProblems.AssertProblemAsync(after, HttpStatusCode.Unauthorized);
        Assert.Equal(RefreshRejectedCode, await StarterProblems.AssertProblemAsync(refresh, HttpStatusCode.BadRequest));
        Assert.Equal(InvalidCredentialsCode, await StarterProblems.AssertProblemAsync(login, HttpStatusCode.BadRequest));
    }

    [Fact]
    public async Task LogoutEndsOnlyItsOwnSession()
    {
        var user = await host.CreateActivatedUserAsync();
        using var client = host.CreateClient();
        var ended = await client.LoginAsync(user.UserName, user.Password);
        var other = await client.LoginAsync(user.UserName, user.Password);

        using var logout = await client.SendAsync(HttpMethod.Post, StarterClient.Paths.Logout, ended.AccessToken);
        using var endedCall = await client.GetAsync(StarterClient.Paths.MyPermissions, ended.AccessToken);
        using var otherCall = await client.GetAsync(StarterClient.Paths.MyPermissions, other.AccessToken);

        Assert.Equal(HttpStatusCode.NoContent, logout.StatusCode);
        await StarterProblems.AssertProblemAsync(endedCall, HttpStatusCode.Unauthorized);
        Assert.Equal(HttpStatusCode.OK, otherCall.StatusCode);
        Assert.Contains(AuthEventType.Logout, await EventTypesAsync(user.UserId));
    }

    [Fact]
    public async Task TheTrailStoresNoTypedNameOrPassword()
    {
        var user = await host.CreateActivatedUserAsync();
        using var client = host.CreateClient();
        using var wrong = await client.PostLoginAsync(user.UserName, AccountsHost.NewPassword());

        await using var scope = host.Factory.Services.CreateAsyncScope();
        var rows = await scope.ServiceProvider.GetRequiredService<IdentityDbContext>().AuthEvents.AsNoTracking()
            .Where(row => row.UserId == user.UserId).ToListAsync();

        var failed = Assert.Single(rows, row => row.EventType == AuthEventType.LoginFailed);
        Assert.Equal(AuthFailureReason.WrongPassword, failed.FailureReason);
        Assert.Equal(SignInMode.Basic, failed.SignInMode);
        Assert.False(string.IsNullOrEmpty(failed.UserNameHash));
        Assert.All(rows, row => Assert.NotEqual(user.UserName, row.UserNameHash));
    }

    private async Task<IReadOnlyList<AuthEventType>> EventTypesAsync(long userId)
    {
        await using var scope = host.Factory.Services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<IdentityDbContext>().AuthEvents.AsNoTracking()
            .Where(row => row.UserId == userId)
            .Select(row => row.EventType)
            .ToListAsync();
    }
}
