using Starter.Application.Results;
using Starter.Domain.Users;
using Starter.Modules.Identity.Application;
using Starter.Modules.Identity.Domain;
using Starter.Modules.Identity.Tests.Fakes;

namespace Starter.Modules.Identity.Tests.Application;

public sealed class LockoutLoginTests
{
    private const string Passphrase = "correct horse";

    [Fact]
    public async Task Reaching_the_threshold_locks_the_account_and_records_the_lockout()
    {
        var harness = new IdentityHarness();
        var user = harness.Store.AddAccount("alice", Passphrase);

        for (var attempt = 0; attempt < IdentityHarness.Lockout.MaxFailedAttempts; attempt++)
        {
            AssertInvalidCredentials(await LoginAsync(harness, "alice", "wrong"));
        }

        Assert.True(user.IsLockedOut(harness.Clock.GetUtcNow()));
        var lockout = Assert.Single(harness.Audit.Events, e => e.EventType == AuthEventType.AccountLockedOut);
        Assert.Equal(user.Id, lockout.UserId);
        Assert.Equal(IdentityHarness.Lockout.MaxFailedAttempts, harness.Audit.Events.Count(e => e.EventType == AuthEventType.LoginFailed));
    }

    [Fact]
    public async Task A_locked_account_is_refused_even_with_the_right_password_until_the_window_ends()
    {
        var harness = new IdentityHarness();
        harness.Store.AddAccount("alice", Passphrase);
        await LockOutAsync(harness);

        var whileLocked = await LoginAsync(harness, "alice", Passphrase);
        AssertInvalidCredentials(whileLocked);
        Assert.Equal(AuthFailureReason.LockedOut, harness.Audit.Events[^1].Reason);
        Assert.Empty(harness.Store.Sessions);

        harness.Clock.Advance(IdentityHarness.Lockout.Duration);
        var afterWindow = await LoginAsync(harness, "alice", Passphrase);

        Assert.True(afterWindow.IsSuccess);
        Assert.Equal(AuthEventType.LoginSucceeded, harness.Audit.Events[^1].EventType);
    }

    [Fact]
    public async Task A_locked_check_still_costs_one_hash_verification()
    {
        var harness = new IdentityHarness();
        harness.Store.AddAccount("alice", Passphrase);
        await LockOutAsync(harness);
        var before = harness.Hasher.VerifyCount;

        await LoginAsync(harness, "alice", Passphrase);

        Assert.Equal(before + 1, harness.Hasher.VerifyCount);
    }

    [Fact]
    public async Task A_success_resets_the_counter()
    {
        var harness = new IdentityHarness();
        var user = harness.Store.AddAccount("alice", Passphrase);
        await LoginAsync(harness, "alice", "wrong");
        await LoginAsync(harness, "alice", "wrong");

        await harness.SignInAsync("alice", Passphrase);
        await LoginAsync(harness, "alice", "wrong");
        await LoginAsync(harness, "alice", "wrong");

        Assert.False(user.IsLockedOut(harness.Clock.GetUtcNow()));
        Assert.Equal(2, user.FailedAccessCount);
    }

    [Fact]
    public async Task An_unactivated_or_directory_bound_account_is_refused_without_counting()
    {
        var harness = new IdentityHarness();
        var pending = harness.AddAdministered("pending", AccountBinding.Password);
        var directory = harness.AddAdministered("directory", AccountBinding.ActiveDirectory);

        AssertInvalidCredentials(await LoginAsync(harness, "pending", Passphrase));
        Assert.Equal(AuthFailureReason.PasswordNotSet, harness.Audit.Events[^1].Reason);
        AssertInvalidCredentials(await LoginAsync(harness, "directory", Passphrase));
        Assert.Equal(AuthFailureReason.BindingMismatch, harness.Audit.Events[^1].Reason);

        Assert.Equal(0, pending.FailedAccessCount);
        Assert.Equal(0, directory.FailedAccessCount);
        Assert.Equal(2, harness.Hasher.VerifyCount);
    }

    [Fact]
    public async Task Refusals_record_the_real_reason_and_the_typed_name()
    {
        var harness = new IdentityHarness();
        var user = harness.Store.AddAccount("alice", Passphrase);
        user.Deactivate();

        await LoginAsync(harness, "nobody", Passphrase);
        await LoginAsync(harness, "alice", "wrong");
        await LoginAsync(harness, "alice", Passphrase);

        Assert.Collection(
            harness.Audit.Events,
            e => Assert.Equal((AuthFailureReason.UnknownAccount, (long?)null, "nobody"), (e.Reason!.Value, e.UserId, e.UserName)),
            e => Assert.Equal((AuthFailureReason.WrongPassword, (long?)user.Id), (e.Reason!.Value, e.UserId)),
            e => Assert.Equal((AuthFailureReason.AccountInactive, SignInMode.Basic), (e.Reason!.Value, e.Mode!.Value)));
    }

    [Fact]
    public async Task A_success_starts_a_basic_session_and_records_it()
    {
        var harness = new IdentityHarness();
        var user = harness.Store.AddAccount("alice", Passphrase);

        await harness.SignInAsync("alice", Passphrase);

        var session = Assert.Single(harness.Store.Sessions);
        Assert.Equal(SignInMode.Basic, session.SignInMode);
        Assert.Null(session.IdTokenHint);
        var recorded = Assert.Single(harness.Audit.Events);
        Assert.Equal(AuthEventType.LoginSucceeded, recorded.EventType);
        Assert.Equal(user.Id, recorded.UserId);
        Assert.Equal(SignInMode.Basic, recorded.Mode);
        Assert.Null(recorded.Reason);
    }

    private static async Task LockOutAsync(IdentityHarness harness)
    {
        for (var attempt = 0; attempt < IdentityHarness.Lockout.MaxFailedAttempts; attempt++)
        {
            await LoginAsync(harness, "alice", "wrong");
        }
    }

    private static Task<Outcome<LoginResult>> LoginAsync(IdentityHarness harness, string userName, string password) =>
        harness.Login().ExecuteAsync(new LoginCommand(userName, password), CancellationToken.None);

    private static void AssertInvalidCredentials(Outcome<LoginResult> outcome)
    {
        Assert.False(outcome.IsSuccess);
        Assert.Equal(IdentityErrorCodes.InvalidCredentials, outcome.Refusal.Code);
    }
}
