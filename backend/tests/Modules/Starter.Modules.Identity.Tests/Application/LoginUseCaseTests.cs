using Starter.Application.Results;
using Starter.Application.Validation;
using Starter.Modules.Identity.Application;
using Starter.Modules.Identity.Tests.Fakes;

namespace Starter.Modules.Identity.Tests.Application;

public sealed class LoginUseCaseTests
{
    private const string Passphrase = "correct horse";

    [Fact]
    public async Task Valid_credentials_start_a_session_with_tokens_and_the_idle_timeout()
    {
        var harness = new IdentityHarness();
        var user = harness.Store.AddAccount("alice", Passphrase);

        var outcome = await harness.Login().ExecuteAsync(new LoginCommand(" ALICE ", Passphrase), CancellationToken.None);

        Assert.True(outcome.IsSuccess);
        var session = Assert.Single(harness.Store.Sessions);
        Assert.Equal(user.Id, session.UserId);
        Assert.Equal(user.Id, outcome.Value.UserId);
        Assert.Equal(user.DisplayName, outcome.Value.DisplayName);
        Assert.Equal("access-1", outcome.Value.AccessToken);
        Assert.Equal(session.SessionId + ".secret1", outcome.Value.RefreshToken);
        Assert.Equal(TestClock.Start + IdentityHarness.Settings.AccessTokenLifetime, outcome.Value.AccessTokenExpiresAt);
        Assert.Equal(30, outcome.Value.IdleTimeoutMinutes);
        var claims = Assert.Single(harness.AccessTokens.Issued);
        Assert.Equal(user.SubjectId, claims.SubjectId);
        Assert.Equal(session.SessionId, claims.SessionId);
    }

    [Fact]
    public async Task The_session_carries_the_current_session_version()
    {
        var harness = new IdentityHarness();
        var user = harness.Store.AddAccount("alice", Passphrase);
        harness.Versions.Versions[user.SubjectId] = 4;

        await harness.SignInAsync("alice", Passphrase);

        Assert.Equal(4, Assert.Single(harness.Store.Sessions).SessionVersion);
        Assert.Equal(4, Assert.Single(harness.AccessTokens.Issued).SessionVersion);
    }

    [Fact]
    public async Task A_wrong_password_is_refused_with_the_credentials_code_and_starts_no_session()
    {
        var harness = new IdentityHarness();
        harness.Store.AddAccount("alice", Passphrase);

        var outcome = await harness.Login().ExecuteAsync(new LoginCommand("alice", "wrong"), CancellationToken.None);

        AssertInvalidCredentials(outcome);
        Assert.Empty(harness.Store.Sessions);
    }

    [Fact]
    public async Task An_unknown_user_gets_the_same_refusal_after_the_same_hashing_work()
    {
        var harness = new IdentityHarness();

        var outcome = await harness.Login().ExecuteAsync(new LoginCommand("nobody", Passphrase), CancellationToken.None);

        AssertInvalidCredentials(outcome);
        Assert.Equal(1, harness.Hasher.VerifyCount);
    }

    [Fact]
    public async Task An_inactive_user_is_refused_even_with_the_right_password()
    {
        var harness = new IdentityHarness();
        harness.Store.AddAccount("alice", Passphrase).Deactivate();

        var outcome = await harness.Login().ExecuteAsync(new LoginCommand("alice", Passphrase), CancellationToken.None);

        AssertInvalidCredentials(outcome);
        Assert.Empty(harness.Store.Sessions);
    }

    [Fact]
    public async Task Missing_fields_are_refused_with_every_field_error()
    {
        var harness = new IdentityHarness();

        var outcome = await harness.Login().ExecuteAsync(new LoginCommand(UserName: null, Password: " "), CancellationToken.None);

        Assert.False(outcome.IsSuccess);
        Assert.Equal(PlatformErrorCodes.Validation, outcome.Refusal.Code);
        Assert.Equal([ValidationMessages.Required], outcome.Refusal.FieldErrors["userName"]);
        Assert.Equal([ValidationMessages.Required], outcome.Refusal.FieldErrors["password"]);
    }

    [Fact]
    public async Task An_outdated_hash_is_replaced_on_a_successful_login()
    {
        var harness = new IdentityHarness();
        var user = harness.Store.AddAccount("alice", Passphrase);
        user.ReplacePasswordHash(PlainTextHasher.OutdatedHashOf(Passphrase));

        await harness.SignInAsync("alice", Passphrase);

        Assert.Equal(PlainTextHasher.HashOf(Passphrase), user.PasswordHash);
    }

    private static void AssertInvalidCredentials(Outcome<SignedInSession> outcome)
    {
        Assert.False(outcome.IsSuccess);
        Assert.Equal(RefusalCategory.Invalid, outcome.Refusal.Category);
        Assert.Equal(IdentityErrorCodes.InvalidCredentials, outcome.Refusal.Code);
        Assert.Empty(outcome.Refusal.FieldErrors);
    }
}
