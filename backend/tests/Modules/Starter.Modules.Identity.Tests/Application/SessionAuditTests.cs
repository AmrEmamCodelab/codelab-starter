using Starter.Domain.Users;
using Starter.Modules.Identity.Application;
using Starter.Modules.Identity.Domain;
using Starter.Modules.Identity.Tests.Fakes;

namespace Starter.Modules.Identity.Tests.Application;

public sealed class SessionAuditTests
{
    private const string Passphrase = "correct horse";

    [Fact]
    public async Task A_reused_refresh_token_records_the_reuse()
    {
        var harness = new IdentityHarness();
        var user = harness.Store.AddAccount("alice", Passphrase);
        var first = await harness.SignInAsync("alice", Passphrase);
        await harness.Refresh().ExecuteAsync(new RefreshCommand(first.RefreshToken), CancellationToken.None);

        await harness.Refresh().ExecuteAsync(new RefreshCommand(first.RefreshToken), CancellationToken.None);

        var rejected = harness.Audit.Events[^1];
        Assert.Equal((AuthEventType.RefreshRejected, AuthFailureReason.RefreshTokenReused, (long?)user.Id), (rejected.EventType, rejected.Reason!.Value, rejected.UserId));
    }

    [Theory]
    [InlineData("not a token", AuthFailureReason.RefreshTokenInvalid)]
    [InlineData("0123456789abcdef0123456789abcdef.secret", AuthFailureReason.RefreshTokenInvalid)]
    public async Task An_unusable_refresh_token_records_why(string token, AuthFailureReason reason)
    {
        var harness = new IdentityHarness();

        await harness.Refresh().ExecuteAsync(new RefreshCommand(token), CancellationToken.None);

        Assert.Equal(reason, Assert.Single(harness.Audit.Events).Reason);
    }

    [Fact]
    public async Task A_deactivated_users_refresh_records_a_stale_session_and_ends_it()
    {
        var harness = new IdentityHarness();
        var user = harness.Store.AddAccount("alice", Passphrase);
        var session = await harness.SignInAsync("alice", Passphrase);
        await harness.Administration().SetActiveAsync(user.Id, isActive: false, CancellationToken.None);

        var outcome = await harness.Refresh().ExecuteAsync(new RefreshCommand(session.RefreshToken), CancellationToken.None);

        Assert.Equal(IdentityErrorCodes.RefreshRejected, outcome.Refusal!.Code);
        Assert.Equal(AuthFailureReason.SessionVersionStale, harness.Audit.Events[^1].Reason);
        Assert.NotNull(Assert.Single(harness.Store.Sessions).RevokedAt);
    }

    [Fact]
    public async Task An_idle_session_records_idle()
    {
        var harness = new IdentityHarness();
        harness.Store.AddAccount("alice", Passphrase);
        var session = await harness.SignInAsync("alice", Passphrase);
        harness.Clock.Advance(IdentityHarness.Settings.IdleTimeout + TimeSpan.FromMinutes(1));

        await harness.Refresh().ExecuteAsync(new RefreshCommand(session.RefreshToken), CancellationToken.None);

        Assert.Equal(AuthFailureReason.SessionIdle, harness.Audit.Events[^1].Reason);
    }

    [Fact]
    public async Task Logout_records_the_session_user_and_mode_and_leaves_other_sessions_open()
    {
        var harness = new IdentityHarness();
        var user = harness.Store.AddAccount("alice", Passphrase);
        await harness.SignInAsync("alice", Passphrase);
        await harness.SignInAsync("alice", Passphrase);
        var (ended, other) = (harness.Store.Sessions[0], harness.Store.Sessions[1]);

        await harness.Logout(ended.SessionId).ExecuteAsync(CancellationToken.None);

        var logout = harness.Audit.Events[^1];
        Assert.Equal((AuthEventType.Logout, (long?)user.Id, SignInMode.Basic), (logout.EventType, logout.UserId, logout.Mode!.Value));
        Assert.NotNull(ended.RevokedAt);
        Assert.Null(other.RevokedAt);
        Assert.False(harness.Revocation.Revoked.ContainsKey(other.SessionId));
        Assert.Empty(harness.Versions.Versions);
    }

    [Fact]
    public async Task The_directory_reports_kind_and_organization()
    {
        var harness = new IdentityHarness();
        var account = UserAccount.Register(new AccountRegistration(
            Guid.NewGuid(),
            "ext",
            new AccountProfile("Ext", null, null, null, UserKind.External, AccountBinding.Sso, OrganizationId: 31),
            TestClock.Start));
        harness.Store.Add(account);
        await harness.Store.SaveChangesAsync(CancellationToken.None);

        var found = await new UserDirectory(harness.Store).FindBySubjectAsync(account.SubjectId, CancellationToken.None);

        Assert.Equal((UserKind.External, (long?)31, false), (found!.Kind, found.OrganizationId, found.HoldsAllPermissions));
    }
}
