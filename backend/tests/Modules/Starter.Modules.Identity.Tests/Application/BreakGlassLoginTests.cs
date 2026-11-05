using Starter.Application.Results;
using Starter.Modules.Identity.Application;
using Starter.Modules.Identity.Application.LoginModes;
using Starter.Modules.Identity.Domain;
using Starter.Modules.Identity.Tests.Fakes;
using Starter.Modules.Identity.Tests.LoginModes;

namespace Starter.Modules.Identity.Tests.Application;

/// <summary>Basic login while <c>Auth:Modes</c> does not list basic: the break-glass administrator only.</summary>
public sealed class BreakGlassLoginTests
{
    private const string Passphrase = "correct horse battery";

    [Theory]
    [InlineData(SignInMode.Sso)]
    [InlineData(SignInMode.ActiveDirectory)]
    public async Task The_break_glass_administrator_signs_in_when_basic_is_off(SignInMode onlyMode)
    {
        var harness = new IdentityHarness { LoginModes = new FakeLoginModes(onlyMode) };
        var admin = harness.Store.AddAccount("admin", Passphrase, isBreakGlass: true);

        var outcome = await harness.Login().ExecuteAsync(new LoginCommand("admin", Passphrase), CancellationToken.None);

        Assert.True(outcome.IsSuccess);
        Assert.Equal(admin.Id, Assert.Single(harness.Store.Sessions).UserId);
    }

    [Fact]
    public async Task An_ordinary_password_account_gets_the_disabled_mode_answer_even_with_the_right_password()
    {
        var harness = new IdentityHarness { LoginModes = new FakeLoginModes(SignInMode.Sso) };
        var user = harness.Store.AddAccount("alice", Passphrase);

        var outcome = await harness.Login().ExecuteAsync(new LoginCommand("alice", Passphrase), CancellationToken.None);

        AssertModeDisabled(outcome);
        Assert.Empty(harness.Store.Sessions);
        Assert.Equal(1, harness.Hasher.VerifyCount);
        var refused = Assert.Single(harness.Audit.Events);
        Assert.Equal((AuthEventType.LoginFailed, AuthFailureReason.LoginModeDisabled, (long?)user.Id), (refused.EventType, refused.Reason, refused.UserId));
    }

    [Fact]
    public async Task Every_refusal_looks_the_same_while_basic_is_off()
    {
        var harness = new IdentityHarness { LoginModes = new FakeLoginModes(SignInMode.Sso) };
        harness.Store.AddAccount("admin", Passphrase, isBreakGlass: true);
        harness.Store.AddAccount("alice", Passphrase);

        var wrongBreakGlass = await harness.Login().ExecuteAsync(new LoginCommand("admin", "wrong"), CancellationToken.None);
        var unknown = await harness.Login().ExecuteAsync(new LoginCommand("nobody", Passphrase), CancellationToken.None);
        var ordinary = await harness.Login().ExecuteAsync(new LoginCommand("alice", "wrong"), CancellationToken.None);
        var invalid = await harness.Login().ExecuteAsync(new LoginCommand(UserName: null, Password: null), CancellationToken.None);

        AssertModeDisabled(wrongBreakGlass);
        AssertModeDisabled(unknown);
        AssertModeDisabled(ordinary);
        AssertModeDisabled(invalid);
        Assert.Empty(harness.Store.Sessions);
    }

    [Fact]
    public async Task A_closed_mode_never_counts_toward_an_ordinary_accounts_lockout()
    {
        var harness = new IdentityHarness { LoginModes = new FakeLoginModes(SignInMode.Sso) };
        var user = harness.Store.AddAccount("alice", Passphrase);

        for (var attempt = 0; attempt < IdentityHarness.Lockout.MaxFailedAttempts + 1; attempt++)
        {
            await harness.Login().ExecuteAsync(new LoginCommand("alice", "wrong"), CancellationToken.None);
        }

        Assert.Equal(0, user.FailedAccessCount);
        Assert.DoesNotContain(harness.Audit.Events, record => record.EventType == AuthEventType.AccountLockedOut);
    }

    [Fact]
    public async Task With_basic_on_nothing_changes_for_ordinary_accounts()
    {
        var harness = new IdentityHarness { LoginModes = new FakeLoginModes(SignInMode.Basic, SignInMode.Sso) };
        harness.Store.AddAccount("alice", Passphrase);

        var signedIn = await harness.Login().ExecuteAsync(new LoginCommand("alice", Passphrase), CancellationToken.None);
        var refused = await harness.Login().ExecuteAsync(new LoginCommand("alice", "wrong"), CancellationToken.None);

        Assert.True(signedIn.IsSuccess);
        Assert.Equal((RefusalCategory.Invalid, IdentityErrorCodes.InvalidCredentials), (refused.Refusal!.Category, refused.Refusal.Code));
    }

    private static void AssertModeDisabled(Outcome<LoginResult> outcome) =>
        Assert.Equal((RefusalCategory.NotFound, LoginModeErrorCodes.LoginModeDisabled), (outcome.Refusal!.Category, outcome.Refusal.Code));
}
