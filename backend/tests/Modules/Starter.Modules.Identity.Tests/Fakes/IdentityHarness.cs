using Microsoft.Extensions.Time.Testing;
using Starter.Domain.Users;
using Starter.Modules.Identity.Application;
using Starter.Modules.Identity.Application.Accounts;
using Starter.Modules.Identity.Application.Activation;
using Starter.Modules.Identity.Application.LoginModes;
using Starter.Modules.Identity.Domain;

namespace Starter.Modules.Identity.Tests.Fakes;

/// <summary>The Application layer wired to fakes and a fake clock.</summary>
internal sealed class IdentityHarness
{
    public static readonly SessionSettings Settings = new(
        AccessTokenLifetime: TimeSpan.FromMinutes(15),
        RefreshTokenLifetime: TimeSpan.FromDays(7),
        IdleTimeout: TimeSpan.FromMinutes(30));

    public static readonly LockoutPolicy Lockout = new(MaxFailedAttempts: 3, Duration: TimeSpan.FromMinutes(15));

    public static readonly PasswordPolicy Passwords = new(MinLength: 12, MinDistinctCharacters: 5);

    public IdentityHarness()
    {
        Tokens = new SessionTokens(AccessTokens, Generator, Settings, Clock);
        Issuer = new SessionIssuer(Store, Store, Tokens, Versions, Revocation);
        Tickets = new FakeActivationTickets(Clock);
        Writes = new AccountWrites(Store, Store, Versions, Hasher, Clock);
    }

    public FakeTimeProvider Clock { get; } = new(TestClock.Start);

    public InMemoryIdentityStore Store { get; } = new();

    public PlainTextHasher Hasher { get; } = new();

    public RecordingAccessTokenIssuer AccessTokens { get; } = new();

    public SequentialTokenGenerator Generator { get; } = new();

    public FakeSessionVersions Versions { get; } = new();

    public FakeSessionRevocation Revocation { get; } = new();

    public RecordingAuthEventSink Audit { get; } = new();

    public FakeActivationTickets Tickets { get; }

    public SessionTokens Tokens { get; }

    public SessionIssuer Issuer { get; }

    public AccountWrites Writes { get; }

    /// <summary>The login modes basic login sees (basic only by default).</summary>
    public ILoginModes LoginModes { get; set; } = new BasicOnlyLoginModes();

    public LoginUseCase Login() =>
        new(new LoginCommandValidator(), new BasicLoginAccounts(Store, LoginModes), new PasswordCheck(Hasher, Lockout, Store, Clock), Issuer, Audit);

    public RefreshSessionUseCase Refresh() => new(new RefreshCommandValidator(), Store, Store, Issuer, Audit);

    public LogoutUseCase Logout(string? sessionId) => new(new FakeCurrentUser(sessionId), Store, Issuer, Audit);

    public SignInExternalAccount ExternalSignIn() => new(Store, Issuer, Audit);

    public CurrentSessionQuery CurrentSession(string? sessionId) => new(new FakeCurrentUser(sessionId), Store);

    public AccountAdministration Administration() =>
        new(Store, Writes, new AccountInputValidator(), new ActivationIssuer(Tickets, Writes, Audit), Audit);

    public ActivateAccountUseCase Activate() =>
        new(new ActivateAccountCommandValidator(Passwords), Tickets, Store, Writes, Audit);

    public async Task<LoginResult> SignInAsync(string userName, string password)
    {
        var outcome = await Login().ExecuteAsync(new LoginCommand(userName, password), CancellationToken.None);
        return outcome.Value;
    }

    /// <summary>Adds and saves an administered account without a password.</summary>
    public UserAccount AddAdministered(string userName, AccountBinding binding, string? email = null, string? loginName = null, string? upn = null)
    {
        var account = UserAccount.Register(new AccountRegistration(
            SubjectId: Guid.NewGuid(),
            UserName: userName,
            Profile: new AccountProfile(
                DisplayName: userName + " display",
                Email: email,
                LoginName: loginName,
                Upn: upn,
                Kind: UserKind.External,
                Binding: binding,
                OrganizationId: null),
            CreatedAt: Clock.GetUtcNow()));
        Store.Add(account);
        Store.SaveChangesAsync(CancellationToken.None).GetAwaiter().GetResult();
        return account;
    }
}

/// <summary>The default deployment: basic login only.</summary>
internal sealed class BasicOnlyLoginModes : ILoginModes
{
    public IReadOnlyList<SignInMode> Enabled { get; } = [SignInMode.Basic];

    public bool IsEnabled(SignInMode mode) => mode == SignInMode.Basic;
}
