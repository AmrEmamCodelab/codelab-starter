using Starter.Application.Results;
using Starter.Modules.Identity.Application;
using Starter.Modules.Identity.Tests.Fakes;

namespace Starter.Modules.Identity.Tests.Application;

public sealed class RefreshSessionUseCaseTests
{
    private const string Passphrase = "correct horse";

    [Fact]
    public async Task A_refresh_rotates_the_refresh_token_and_issues_a_new_access_token()
    {
        var harness = new IdentityHarness();
        harness.Store.AddAccount("alice", Passphrase);
        var signedIn = await harness.SignInAsync("alice", Passphrase);
        harness.Clock.Advance(TimeSpan.FromMinutes(10));

        var outcome = await harness.Refresh().ExecuteAsync(new RefreshCommand(signedIn.RefreshToken), CancellationToken.None);

        Assert.True(outcome.IsSuccess);
        Assert.NotEqual(signedIn.RefreshToken, outcome.Value.RefreshToken);
        Assert.NotEqual(signedIn.AccessToken, outcome.Value.AccessToken);
        Assert.Equal(harness.Clock.GetUtcNow() + IdentityHarness.Settings.AccessTokenLifetime, outcome.Value.AccessTokenExpiresAt);
        var session = Assert.Single(harness.Store.Sessions);
        Assert.Equal(1, session.RefreshGeneration);
        Assert.Equal(harness.Clock.GetUtcNow(), session.LastActivityAt);
        Assert.All(harness.AccessTokens.Issued, claims => Assert.Equal(session.SessionId, claims.SessionId));
    }

    [Fact]
    public async Task A_reused_refresh_token_is_refused_and_ends_the_session()
    {
        var harness = new IdentityHarness();
        harness.Store.AddAccount("alice", Passphrase);
        var signedIn = await harness.SignInAsync("alice", Passphrase);
        var rotated = await harness.Refresh().ExecuteAsync(new RefreshCommand(signedIn.RefreshToken), CancellationToken.None);

        var reuse = await harness.Refresh().ExecuteAsync(new RefreshCommand(signedIn.RefreshToken), CancellationToken.None);
        var afterTheft = await harness.Refresh().ExecuteAsync(new RefreshCommand(rotated.Value.RefreshToken), CancellationToken.None);

        AssertRejected(reuse);
        AssertRejected(afterTheft);
        var session = Assert.Single(harness.Store.Sessions);
        Assert.NotNull(session.RevokedAt);
        Assert.True(harness.Revocation.Revoked.ContainsKey(session.SessionId));
    }

    [Fact]
    public async Task A_session_idle_longer_than_the_timeout_is_refused()
    {
        var harness = new IdentityHarness();
        harness.Store.AddAccount("alice", Passphrase);
        var signedIn = await harness.SignInAsync("alice", Passphrase);
        harness.Clock.Advance(IdentityHarness.Settings.IdleTimeout + TimeSpan.FromSeconds(1));

        AssertRejected(await harness.Refresh().ExecuteAsync(new RefreshCommand(signedIn.RefreshToken), CancellationToken.None));
    }

    [Fact]
    public async Task A_session_past_its_absolute_lifetime_is_refused_even_when_kept_active()
    {
        var harness = new IdentityHarness();
        harness.Store.AddAccount("alice", Passphrase);
        var token = (await harness.SignInAsync("alice", Passphrase)).RefreshToken;
        var step = TimeSpan.FromMinutes(25);
        Outcome<SignedInSession> outcome;
        do
        {
            harness.Clock.Advance(step);
            outcome = await harness.Refresh().ExecuteAsync(new RefreshCommand(token), CancellationToken.None);
            token = outcome.IsSuccess ? outcome.Value.RefreshToken : token;
        }
        while (outcome.IsSuccess);

        AssertRejected(outcome);
        Assert.True(harness.Clock.GetUtcNow() >= TestClock.Start + IdentityHarness.Settings.RefreshTokenLifetime);
    }

    [Fact]
    public async Task A_bumped_session_version_refuses_the_refresh_and_ends_the_session()
    {
        var harness = new IdentityHarness();
        var user = harness.Store.AddAccount("alice", Passphrase);
        var signedIn = await harness.SignInAsync("alice", Passphrase);
        harness.Versions.Versions[user.SubjectId] = 1;

        AssertRejected(await harness.Refresh().ExecuteAsync(new RefreshCommand(signedIn.RefreshToken), CancellationToken.None));
        Assert.NotNull(Assert.Single(harness.Store.Sessions).RevokedAt);
    }

    [Fact]
    public async Task A_deactivated_user_cannot_refresh()
    {
        var harness = new IdentityHarness();
        var user = harness.Store.AddAccount("alice", Passphrase);
        var signedIn = await harness.SignInAsync("alice", Passphrase);
        user.Deactivate();

        AssertRejected(await harness.Refresh().ExecuteAsync(new RefreshCommand(signedIn.RefreshToken), CancellationToken.None));
    }

    [Fact]
    public async Task A_refresh_after_logout_is_refused()
    {
        var harness = new IdentityHarness();
        harness.Store.AddAccount("alice", Passphrase);
        var signedIn = await harness.SignInAsync("alice", Passphrase);
        await harness.Logout(harness.Store.Sessions[0].SessionId).ExecuteAsync(CancellationToken.None);

        AssertRejected(await harness.Refresh().ExecuteAsync(new RefreshCommand(signedIn.RefreshToken), CancellationToken.None));
    }

    [Theory]
    [InlineData("not-a-token")]
    [InlineData("5f0e2c9a7b3d4e1f8a6c0b2d9e7f1a3c.secret")]
    public async Task A_malformed_or_unknown_token_is_refused(string token)
    {
        var harness = new IdentityHarness();

        AssertRejected(await harness.Refresh().ExecuteAsync(new RefreshCommand(token), CancellationToken.None));
    }

    [Fact]
    public async Task A_missing_token_is_a_validation_refusal()
    {
        var harness = new IdentityHarness();

        var outcome = await harness.Refresh().ExecuteAsync(new RefreshCommand(RefreshToken: null), CancellationToken.None);

        Assert.Equal(PlatformErrorCodes.Validation, outcome.Refusal?.Code);
    }

    private static void AssertRejected(Outcome<SignedInSession> outcome)
    {
        Assert.False(outcome.IsSuccess);
        Assert.Equal(RefusalCategory.Invalid, outcome.Refusal.Category);
        Assert.Equal(IdentityErrorCodes.RefreshRejected, outcome.Refusal.Code);
    }
}
