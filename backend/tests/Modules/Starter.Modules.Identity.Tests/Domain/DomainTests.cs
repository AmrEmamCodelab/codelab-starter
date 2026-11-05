using Starter.Modules.Identity.Domain;
using Starter.Modules.Identity.Tests.Fakes;

namespace Starter.Modules.Identity.Tests.Domain;

public sealed class RefreshTokenTests
{
    private const string SessionId = "5f0e2c9a7b3d4e1f8a6c0b2d9e7f1a3c";

    [Fact]
    public void A_formatted_token_parses_back()
    {
        var token = new RefreshToken(SessionId, "Ab_-9");

        Assert.True(RefreshToken.TryParse(token.Format(), out var parsed));
        Assert.Equal(token, parsed);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("5f0e2c9a7b3d4e1f8a6c0b2d9e7f1a3c")]
    [InlineData("5f0e2c9a7b3d4e1f8a6c0b2d9e7f1a3c.")]
    [InlineData("5F0E2C9A7B3D4E1F8a6c0b2d9e7f1a3c.secret")]
    [InlineData("short.secret")]
    [InlineData("5f0e2c9a7b3d4e1f8a6c0b2d9e7f1a3c.sec+ret")]
    public void Anything_else_is_refused_without_throwing(string? text) =>
        Assert.False(RefreshToken.TryParse(text, out _));

    [Fact]
    public void An_overlong_token_is_refused() =>
        Assert.False(RefreshToken.TryParse(SessionId + "." + new string('a', IdentityLimits.RefreshTokenMaxLength), out _));

    [Fact]
    public void The_secret_never_reaches_a_log() =>
        Assert.DoesNotContain("topsecret", new RefreshToken(SessionId, "topsecret").ToString(), StringComparison.Ordinal);
}

public sealed class UserSessionTests
{
    private static readonly TimeSpan Idle = TimeSpan.FromMinutes(30);

    [Fact]
    public void The_current_secret_is_allowed_and_an_old_one_is_a_mismatch()
    {
        var session = Start();
        session.Rotate("second", TestClock.Start.AddMinutes(1));

        Assert.Equal(RefreshDecision.Allowed, session.CheckRefresh("second", TestClock.Start.AddMinutes(2), Idle));
        Assert.Equal(RefreshDecision.SecretMismatch, session.CheckRefresh("first", TestClock.Start.AddMinutes(2), Idle));
        Assert.Equal(1, session.RefreshGeneration);
    }

    [Fact]
    public void Revoked_expired_and_idle_sessions_are_refused_in_that_order()
    {
        var idle = Start();
        var expired = Start(lifetime: TimeSpan.FromMinutes(10));
        var revoked = Start();
        revoked.Revoke(TestClock.Start);

        Assert.Equal(RefreshDecision.Idle, idle.CheckRefresh("first", TestClock.Start + Idle + TimeSpan.FromTicks(1), Idle));
        Assert.Equal(RefreshDecision.Expired, expired.CheckRefresh("first", TestClock.Start.AddMinutes(10), Idle));
        Assert.Equal(RefreshDecision.Revoked, revoked.CheckRefresh("first", TestClock.Start, Idle));
    }

    [Fact]
    public void Revoking_twice_keeps_the_first_time()
    {
        var session = Start();
        session.Revoke(TestClock.Start);
        session.Revoke(TestClock.Start.AddHours(1));

        Assert.Equal(TestClock.Start, session.RevokedAt);
    }

    [Fact]
    public void Only_a_hash_of_the_secret_is_stored()
    {
        var session = Start();

        Assert.Equal(IdentityLimits.RefreshSecretHashBytes, session.RefreshSecretHash.Length);
        Assert.Equal(TestClock.Start.AddDays(7), session.ExpiresAt);
    }

    private static UserSession Start(TimeSpan? lifetime = null) => UserSession.Start(new NewUserSession(
        SessionId: "5f0e2c9a7b3d4e1f8a6c0b2d9e7f1a3c",
        UserId: 7,
        RefreshSecret: "first",
        SessionVersion: 0,
        StartedAt: TestClock.Start,
        Lifetime: lifetime ?? TimeSpan.FromDays(7),
        Mode: SignInMode.Basic,
        IdTokenHint: null));
}

public sealed class UserAccountTests
{
    [Fact]
    public void A_new_account_is_active_with_a_normalized_user_name()
    {
        var account = UserAccount.Create(new NewUserAccount(Guid.NewGuid(), " Alice ", " Alice A ", "hash", IsBreakGlass: false, TestClock.Start));

        Assert.True(account.IsActive);
        Assert.Equal("Alice", account.UserName);
        Assert.Equal("ALICE", account.NormalizedUserName);
        Assert.Equal("Alice A", account.DisplayName);
    }

    [Fact]
    public void Restoring_break_glass_reactivates_the_account()
    {
        var account = UserAccount.Create(new NewUserAccount(Guid.NewGuid(), "admin", "Admin", "hash", IsBreakGlass: false, TestClock.Start));
        account.Deactivate();

        account.RestoreBreakGlass();

        Assert.True(account.IsActive);
        Assert.True(account.IsBreakGlass);
    }

    [Fact]
    public void A_blank_user_name_or_hash_is_refused()
    {
        Assert.Throws<ArgumentException>(() => UserAccount.Create(new NewUserAccount(Guid.NewGuid(), " ", "A", "hash", IsBreakGlass: false, TestClock.Start)));
        Assert.Throws<ArgumentException>(() => UserAccount.Create(new NewUserAccount(Guid.NewGuid(), "a", "A", " ", IsBreakGlass: false, TestClock.Start)));
    }
}
