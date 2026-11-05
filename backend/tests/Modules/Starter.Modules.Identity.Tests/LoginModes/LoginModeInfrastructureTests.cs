using System.Security.Cryptography;
using Codelab.Identity;
using Codelab.Identity.ActiveDirectory;
using Codelab.Identity.Oidc;
using Codelab.Security;
using Codelab.Security.Keys;
using Codelab.Security.Replay;
using Codelab.Security.Tickets;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using Starter.Modules.Identity.Application.LoginModes;
using Starter.Modules.Identity.Domain;
using Starter.Modules.Identity.Infrastructure.LoginModes;
using Starter.Modules.Identity.Tests.Fakes;

namespace Starter.Modules.Identity.Tests.LoginModes;

public sealed class SealedSsoTicketsTests
{
    private readonly FakeTimeProvider _clock = new(TestClock.Start);

    [Fact]
    public async Task A_pending_sign_in_round_trips_once()
    {
        var tickets = Create(enabled: true);
        var pending = new SsoPendingSignIn("state", "verifier", "nonce", "/admin", BindingHash: "hash");

        var sealedValue = tickets.SealPending(pending);
        var first = await tickets.OpenPendingAsync(sealedValue, CancellationToken.None);
        var second = await tickets.OpenPendingAsync(sealedValue, CancellationToken.None);

        Assert.Equal(SealedValueStatus.Opened, first.Status);
        Assert.Equal(pending, first.Value);
        Assert.Equal(SealedValueStatus.Rejected, second.Status);
        Assert.DoesNotContain("verifier", sealedValue, StringComparison.Ordinal);
    }

    [Fact]
    public async Task An_exchange_ticket_expires_after_two_minutes()
    {
        var tickets = Create(enabled: true);
        var ticket = tickets.SealExchange(new SsoExchangeGrant("refresh", BindingHash: null));

        _clock.Advance(SsoLimits.ExchangeLifetime + TimeSpan.FromSeconds(1));

        Assert.Equal(SealedValueStatus.Rejected, (await tickets.OpenExchangeAsync(ticket, CancellationToken.None)).Status);
    }

    [Fact]
    public async Task A_value_sealed_for_one_purpose_never_opens_as_the_other()
    {
        var tickets = Create(enabled: true);
        var pending = tickets.SealPending(new SsoPendingSignIn("state", "verifier", "nonce", "/", BindingHash: null));
        var exchange = tickets.SealExchange(new SsoExchangeGrant("refresh", BindingHash: null));

        Assert.Equal(SealedValueStatus.Rejected, (await tickets.OpenExchangeAsync(pending, CancellationToken.None)).Status);
        Assert.Equal(SealedValueStatus.Rejected, (await tickets.OpenPendingAsync(exchange, CancellationToken.None)).Status);
        Assert.Equal(SealedValueStatus.Rejected, (await tickets.OpenPendingAsync("t1.k1.garbage", CancellationToken.None)).Status);
    }

    [Fact]
    public async Task Disabled_tickets_cannot_seal_and_open_as_unavailable()
    {
        var tickets = Create(enabled: false);

        Assert.Throws<InvalidOperationException>(() => tickets.SealExchange(new SsoExchangeGrant("refresh", BindingHash: null)));
        Assert.Equal(SealedValueStatus.Unavailable, (await tickets.OpenExchangeAsync("anything", CancellationToken.None)).Status);
    }

    private SealedSsoTickets Create(bool enabled)
    {
        var options = new SealedTicketOptions
        {
            Enabled = enabled,
            ActiveKeyId = "k1",
            Keys = [new KeyEntryOptions { KeyId = "k1", MaterialBase64 = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)) }],
        };
        var services = new ServiceCollection()
            .AddSingleton<ISealedTicketService>(new SealedTicketService(options, new MemoryReplayGuard(_clock, entryLimit: 100), _clock, SecurityProtocol.Create("CODELABTEST")))
            .BuildServiceProvider();
        return new SealedSsoTickets(Options.Create(options), services);
    }
}

public sealed class LoginModeAdapterTests
{
    private static readonly DirectoryIdentity Directory = new(LoginName: "erin", Upn: "erin@corp.example.test", Email: "erin@example.test", DisplayName: "Erin");

    [Theory]
    [InlineData(LoginMatchField.Email, AccountMatchField.Email)]
    [InlineData(LoginMatchField.LoginName, AccountMatchField.LoginName)]
    [InlineData(LoginMatchField.Upn, AccountMatchField.Upn)]
    public void Match_fields_map_one_to_one(LoginMatchField field, AccountMatchField expected) => Assert.Equal(expected, MatchFields.From(field));

    [Fact]
    public void An_undefined_match_field_throws() => Assert.Throws<ArgumentOutOfRangeException>(() => MatchFields.From((LoginMatchField)99));

    [Theory]
    [InlineData(LoginMatchField.LoginName, "erin")]
    [InlineData(LoginMatchField.Upn, "erin@corp.example.test")]
    [InlineData(LoginMatchField.Email, "erin@example.test")]
    public async Task A_directory_success_carries_the_configured_match_value(LoginMatchField matchBy, string expected)
    {
        var (adapter, authenticator) = Directory_(matchBy);
        authenticator.Result = new DirectoryAuthResult(DirectoryAuthStatus.Succeeded, Directory);

        var verdict = await adapter.VerifyAsync("erin", "secret", CancellationToken.None);

        Assert.Equal(new DirectoryVerdict(DirectoryVerdictStatus.Accepted, expected), verdict);
        Assert.Equal(MatchFields.From(matchBy), adapter.MatchField);
    }

    [Theory]
    [InlineData(DirectoryAuthStatus.InvalidCredentials, DirectoryVerdictStatus.Refused)]
    [InlineData(DirectoryAuthStatus.AccountDisabled, DirectoryVerdictStatus.AccountDisabled)]
    [InlineData(DirectoryAuthStatus.Unavailable, DirectoryVerdictStatus.Unavailable)]
    public async Task Directory_refusals_map_to_verdicts(DirectoryAuthStatus status, DirectoryVerdictStatus expected)
    {
        var (adapter, authenticator) = Directory_(LoginMatchField.LoginName);
        authenticator.Result = new DirectoryAuthResult(status, Identity: null);

        Assert.Equal(new DirectoryVerdict(expected, MatchValue: null), await adapter.VerifyAsync("erin", "x", CancellationToken.None));
    }

    [Fact]
    public async Task The_sso_adapter_passes_the_pending_values_and_maps_a_success()
    {
        var (adapter, provider) = Sso(LoginMatchField.Upn);
        provider.Result = OidcSignInResult.Succeeded(new OidcIdentity("sub", "e@example.test", "login", "u@example.test", "Name", "id.token.value"));
        var pending = new SsoPendingSignIn("state", "verifier", "nonce", "/", BindingHash: null);

        var result = await adapter.CompleteAsync(code: "code", state: "state", pending, CancellationToken.None);

        Assert.Equal(new SsoIdentityResult(SsoIdentityStatus.Succeeded, MatchValue: "u@example.test", IdToken: "id.token.value"), result);
        Assert.Equal(new OidcCallback("code", "state"), provider.LastCompletion!.Value.Callback);
        Assert.Equal(new OidcPendingAuthorization("state", "verifier", "nonce"), provider.LastCompletion!.Value.Pending);
    }

    [Theory]
    [InlineData(OidcSignInStatus.Rejected, SsoIdentityStatus.Rejected)]
    [InlineData(OidcSignInStatus.Unavailable, SsoIdentityStatus.Unavailable)]
    public async Task Sso_refusals_map_to_statuses(OidcSignInStatus status, SsoIdentityStatus expected)
    {
        var (adapter, provider) = Sso(LoginMatchField.Email);
        provider.Result = status == OidcSignInStatus.Unavailable ? OidcSignInResult.Unavailable() : OidcSignInResult.Rejected(OidcFailureReason.StateMismatch);

        var result = await adapter.CompleteAsync(code: "c", state: "s", new SsoPendingSignIn("s", "v", "n", "/", BindingHash: null), CancellationToken.None);

        Assert.Equal(expected, result.Status);
        Assert.Null(result.IdToken);
    }

    [Fact]
    public void The_sso_adapter_begins_with_the_provider_and_keeps_the_binding()
    {
        var (adapter, provider) = Sso(LoginMatchField.Email);

        var authorization = adapter.Begin(returnUrl: "/x", bindingHash: "hash");
        var endSession = adapter.EndSessionUrl("hint");

        Assert.Equal(new SsoPendingSignIn("s", "v", "n", "/x", "hash"), authorization.Pending);
        Assert.Equal(("hint", (Uri?)null), provider.LastEndSession!.Value);
        Assert.Contains("id_token_hint=hint", endSession.Query, StringComparison.Ordinal);
    }

    private static (ActiveDirectorySignIn Adapter, FakeActiveDirectoryAuthenticator Authenticator) Directory_(LoginMatchField matchBy)
    {
        var authenticator = new FakeActiveDirectoryAuthenticator();
        var services = new ServiceCollection().AddSingleton<IActiveDirectoryAuthenticator>(authenticator).BuildServiceProvider();
        return (new ActiveDirectorySignIn(Options.Create(new ActiveDirectoryOptions { MatchBy = matchBy }), services), authenticator);
    }

    private static (OidcSsoProvider Adapter, FakeOidcIdentityProvider Provider) Sso(LoginMatchField matchBy)
    {
        var provider = new FakeOidcIdentityProvider();
        var services = new ServiceCollection().AddSingleton<IOidcIdentityProvider>(provider).BuildServiceProvider();
        return (new OidcSsoProvider(Options.Create(new OidcOptions { MatchBy = matchBy }), services), provider);
    }
}

public sealed class LoginModesRegistrationTests
{
    [Fact]
    public void Basic_only_registers_no_provider()
    {
        using var provider = Build(new() { });

        Assert.Null(provider.GetService<IActiveDirectoryAuthenticator>());
        Assert.Null(provider.GetService<IOidcIdentityProvider>());
        Assert.Equal([SignInMode.Basic], provider.GetRequiredService<ILoginModes>().Enabled);
    }

    [Fact]
    public void A_listed_directory_switches_its_provider_on_so_that_its_guard_runs()
    {
        using var provider = Build(new() { ["Auth:Modes:0"] = "ad" });

        Assert.NotNull(provider.GetService<IActiveDirectoryAuthenticator>());
        var error = Assert.Throws<OptionsValidationException>(() => provider.GetRequiredService<IOptions<ActiveDirectoryOptions>>().Value);
        Assert.Contains(error.Failures, failure => failure.Contains(nameof(ActiveDirectoryOptions.Host), StringComparison.Ordinal));
    }

    [Fact]
    public void A_listed_sso_switches_its_provider_on_so_that_its_guard_runs()
    {
        using var provider = Build(new() { ["Auth:Modes:0"] = "sso" });

        var error = Assert.Throws<OptionsValidationException>(() => provider.GetRequiredService<IOptions<OidcOptions>>().Value);
        Assert.Contains(error.Failures, failure => failure.Contains(nameof(OidcOptions.Authority), StringComparison.Ordinal));
        Assert.Throws<OptionsValidationException>(() => provider.GetService<IOidcIdentityProvider>());
    }

    [Fact]
    public void An_unknown_mode_fails_the_auth_options()
    {
        using var provider = Build(new() { ["Auth:Modes:0"] = "kerberos" });

        Assert.Throws<OptionsValidationException>(() => provider.GetRequiredService<IOptions<AuthModesOptions>>().Value);
    }

    private static ServiceProvider Build(Dictionary<string, string?> settings)
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(settings).Build();
        var services = new ServiceCollection()
            .AddSingleton<IHostEnvironment>(new TestHostEnvironment(Environments.Production))
            .AddLogging();
        services.AddOptions<SealedTicketOptions>();
        services.AddIdentityLoginModes(configuration);
        return services.BuildServiceProvider();
    }
}
