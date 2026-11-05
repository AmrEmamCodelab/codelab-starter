using Starter.Application.Results;
using Starter.Modules.Identity.Application;
using Starter.Modules.Identity.Tests.Fakes;

namespace Starter.Modules.Identity.Tests.Application;

public sealed class LogoutUseCaseTests
{
    [Fact]
    public async Task Logout_ends_the_session_and_revokes_its_id_until_the_last_access_token_expires()
    {
        var harness = new IdentityHarness();
        harness.Store.AddAccount("alice", "pw");
        await harness.SignInAsync("alice", "pw");
        var session = harness.Store.Sessions[0];
        harness.Clock.Advance(TimeSpan.FromMinutes(5));

        var outcome = await harness.Logout(session.SessionId).ExecuteAsync(CancellationToken.None);

        Assert.True(outcome.IsSuccess);
        Assert.Equal(harness.Clock.GetUtcNow(), session.RevokedAt);
        Assert.Equal(harness.Clock.GetUtcNow() + IdentityHarness.Settings.AccessTokenLifetime, harness.Revocation.Revoked[session.SessionId]);
    }

    [Fact]
    public async Task Logout_of_an_unknown_session_still_revokes_the_session_id()
    {
        var harness = new IdentityHarness();
        const string sessionId = "5f0e2c9a7b3d4e1f8a6c0b2d9e7f1a3c";

        var outcome = await harness.Logout(sessionId).ExecuteAsync(CancellationToken.None);

        Assert.True(outcome.IsSuccess);
        Assert.True(harness.Revocation.Revoked.ContainsKey(sessionId));
    }

    [Fact]
    public async Task A_token_without_a_session_id_is_refused()
    {
        var harness = new IdentityHarness();

        var outcome = await harness.Logout(sessionId: null).ExecuteAsync(CancellationToken.None);

        Assert.False(outcome.IsSuccess);
        Assert.Equal(RefusalCategory.Forbidden, outcome.Refusal.Category);
        Assert.Equal(IdentityErrorCodes.SessionRequired, outcome.Refusal.Code);
        Assert.Empty(harness.Revocation.Revoked);
    }
}
