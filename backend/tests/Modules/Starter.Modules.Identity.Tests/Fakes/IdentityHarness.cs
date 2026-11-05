using Microsoft.Extensions.Time.Testing;
using Starter.Modules.Identity.Application;

namespace Starter.Modules.Identity.Tests.Fakes;

/// <summary>The Application layer wired to fakes and a fake clock.</summary>
internal sealed class IdentityHarness
{
    public static readonly SessionSettings Settings = new(
        AccessTokenLifetime: TimeSpan.FromMinutes(15),
        RefreshTokenLifetime: TimeSpan.FromDays(7),
        IdleTimeout: TimeSpan.FromMinutes(30));

    public IdentityHarness()
    {
        Tokens = new SessionTokens(AccessTokens, Generator, Settings, Clock);
        Issuer = new SessionIssuer(Store, Store, Tokens, Versions, Revocation);
    }

    public FakeTimeProvider Clock { get; } = new(TestClock.Start);

    public InMemoryIdentityStore Store { get; } = new();

    public PlainTextHasher Hasher { get; } = new();

    public RecordingAccessTokenIssuer AccessTokens { get; } = new();

    public SequentialTokenGenerator Generator { get; } = new();

    public FakeSessionVersions Versions { get; } = new();

    public FakeSessionRevocation Revocation { get; } = new();

    public SessionTokens Tokens { get; }

    public SessionIssuer Issuer { get; }

    public LoginUseCase Login() => new(new LoginCommandValidator(), Store, Hasher, Issuer);

    public RefreshSessionUseCase Refresh() => new(new RefreshCommandValidator(), Store, Store, Versions, Issuer);

    public LogoutUseCase Logout(string? sessionId) => new(new FakeCurrentUser(sessionId), Store, Issuer);

    public async Task<SignedInSession> SignInAsync(string userName, string password)
    {
        var outcome = await Login().ExecuteAsync(new LoginCommand(userName, password), CancellationToken.None);
        return outcome.Value;
    }
}
