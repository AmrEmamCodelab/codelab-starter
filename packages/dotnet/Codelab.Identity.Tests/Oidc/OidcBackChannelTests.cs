using System.Net;
using Codelab.Identity.Oidc;
using Codelab.Identity.Tests.TestSupport;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;

namespace Codelab.Identity.Tests.Oidc;

/// <summary>A back channel scripted by the test (the shape of a host's gateway relay adapter).</summary>
internal sealed class ScriptedBackChannel(OidcBackChannel mode = OidcBackChannel.GatewayRelay) : IOidcBackChannel
{
    public OidcBackChannel Mode { get; } = mode;

    public Func<OidcBackChannelResponse> Token { get; set; } = () => OidcBackChannelResponse.Unavailable("not scripted");

    public Func<OidcBackChannelResponse> Jwks { get; set; } = () => OidcBackChannelResponse.Unavailable("not scripted");

    public List<KeyValuePair<string, string>> LastForm { get; } = [];

    public int JwksCalls { get; private set; }

    public Task<OidcBackChannelResponse> RedeemCodeAsync(IReadOnlyCollection<KeyValuePair<string, string>> form, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        LastForm.Clear();
        LastForm.AddRange(form);
        return Task.FromResult(Token());
    }

    public Task<OidcBackChannelResponse> GetJwksAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        JwksCalls++;
        return Task.FromResult(Jwks());
    }
}

/// <summary>The sign-in over a relay back channel: the token exchange and the JWKS fetch never touch the named client.</summary>
public sealed class OidcRelayBackChannelTests : IDisposable
{
    private const string Code = "authorization-code-1";
    private const string State = "state-1";
    private const string Verifier = "verifier-1";
    private const string RelayRejected = "GATEWAY_UPSTREAM_REJECTED";
    private const string RelayUnavailable = "GATEWAY_UPSTREAM_UNAVAILABLE";

    private readonly FakeTimeProvider clock = new(new DateTimeOffset(2026, 11, 7, 9, 0, 0, TimeSpan.Zero));
    private readonly TestIssuer issuer;
    private readonly ScriptedBackChannel relay = new();

    public OidcRelayBackChannelTests()
    {
        issuer = new TestIssuer(clock);
        relay.Jwks = () => OidcBackChannelResponse.Answered((int)HttpStatusCode.OK, issuer.Jwks());
    }

    public void Dispose() => issuer.Dispose();

    [Fact]
    public async Task A_relayed_token_exchange_signs_in_with_keys_fetched_through_the_relay()
    {
        var token = issuer.Token();
        relay.Token = () => OidcBackChannelResponse.Answered((int)HttpStatusCode.OK, StubProvider.TokenResponse(token));

        var result = await Complete();

        Assert.Equal(OidcSignInStatus.Succeeded, result.Status);
        Assert.Equal(TestIssuer.Subject, result.Identity!.Subject);
        Assert.Equal(1, relay.JwksCalls);
    }

    [Fact]
    public async Task The_relayed_form_is_the_token_request_in_order()
    {
        var options = RelayOptions();
        options.ClientSecret = Guid.NewGuid().ToString("N");
        relay.Token = () => OidcBackChannelResponse.Answered((int)HttpStatusCode.OK, StubProvider.TokenResponse(issuer.Token()));

        await Complete(options);

        Assert.Equal(
            [
                new("grant_type", "authorization_code"),
                new("code", Code),
                new("redirect_uri", TestIssuer.RedirectUri),
                new("client_id", TestIssuer.ClientId),
                new("code_verifier", Verifier),
                new KeyValuePair<string, string>("client_secret", options.ClientSecret),
            ],
            relay.LastForm);
    }

    [Fact]
    public async Task A_relay_rejection_is_a_rejected_token_request()
    {
        relay.Token = () => OidcBackChannelResponse.Rejected((int)HttpStatusCode.BadGateway, RelayRejected);

        var result = await Complete();

        Assert.Equal(OidcSignInStatus.Rejected, result.Status);
        Assert.Equal(OidcFailureReason.TokenRequestRejected, result.Failure);
    }

    [Fact]
    public async Task An_unavailable_relay_is_an_unavailable_provider()
    {
        relay.Token = () => OidcBackChannelResponse.Unavailable(RelayUnavailable);

        var result = await Complete();

        Assert.Equal(OidcSignInStatus.Unavailable, result.Status);
        Assert.Equal(OidcFailureReason.ProviderUnavailable, result.Failure);
    }

    [Fact]
    public async Task Keys_the_relay_cannot_fetch_make_the_sign_in_unavailable()
    {
        relay.Token = () => OidcBackChannelResponse.Answered((int)HttpStatusCode.OK, StubProvider.TokenResponse(issuer.Token()));
        relay.Jwks = () => OidcBackChannelResponse.Unavailable(RelayUnavailable);

        var result = await Complete();

        Assert.Equal(OidcSignInStatus.Unavailable, result.Status);
    }

    [Theory]
    [InlineData((int)HttpStatusCode.OK, "not json")]
    [InlineData((int)HttpStatusCode.OK, "[]")]
    [InlineData((int)HttpStatusCode.NotFound, "{\"keys\":[]}")]
    public async Task An_unusable_jwks_answer_yields_no_keys(int status, string body)
    {
        relay.Jwks = () => OidcBackChannelResponse.Answered(status, body);

        Assert.Null(await Source().GetSigningKeysAsync(kid: null, CancellationToken.None));
    }

    [Fact]
    public async Task A_relayed_jwks_is_cached_like_a_direct_one()
    {
        var source = Source();

        var first = await source.GetSigningKeysAsync(issuer.Key.KeyId, CancellationToken.None);
        var second = await source.GetSigningKeysAsync(issuer.Key.KeyId, CancellationToken.None);

        Assert.Equal(issuer.Key.KeyId, Assert.Single(first!).KeyId);
        Assert.Same(first, second);
        Assert.Equal(1, relay.JwksCalls);
    }

    [Fact]
    public void A_response_never_prints_its_body()
    {
        var token = issuer.Token();

        var text = OidcBackChannelResponse.Answered((int)HttpStatusCode.OK, StubProvider.TokenResponse(token)).ToString();

        Assert.DoesNotContain(token, text, StringComparison.Ordinal);
        Assert.Contains(nameof(OidcBackChannelOutcome.Answered), text, StringComparison.Ordinal);
        Assert.Equal($"{nameof(OidcBackChannelOutcome.Rejected)} 502 {RelayRejected}", OidcBackChannelResponse.Rejected(502, RelayRejected).ToString());
    }

    [Fact]
    public void The_enums_have_their_contract_values()
    {
        Assert.Equal(1, (int)OidcBackChannel.Direct);
        Assert.Equal(2, (int)OidcBackChannel.GatewayRelay);
        Assert.Equal(1, (int)OidcBackChannelOutcome.Answered);
        Assert.Equal(2, (int)OidcBackChannelOutcome.Unavailable);
        Assert.Equal(3, (int)OidcBackChannelOutcome.Rejected);
        Assert.Equal(OidcBackChannel.Direct, new OidcOptions().BackChannel);
        Assert.False(new OidcOptions().NoEgress);
    }

    private static OidcOptions RelayOptions()
    {
        var options = TestIssuer.Options();
        options.BackChannel = OidcBackChannel.GatewayRelay;
        options.TokenEndpoint = string.Empty;
        options.JwksUri = string.Empty;
        return options;
    }

    private JwksSigningKeySource Source() => new(relay, clock, NullLogger<JwksSigningKeySource>.Instance);

    private Task<OidcSignInResult> Complete(OidcOptions? options = null)
    {
        var settings = Options.Create(options ?? RelayOptions());
        var validator = new OidcIdTokenValidator(settings, Source(), clock, NullLogger<OidcIdTokenValidator>.Instance);
        var provider = new OidcIdentityProvider(settings, validator, relay, NullLogger<OidcIdentityProvider>.Instance);
        return provider.CompleteAsync(new OidcCallback(Code, State), new OidcPendingAuthorization(State, Verifier, TestIssuer.Nonce), CancellationToken.None);
    }
}

/// <summary>The direct back channel over the named client (a stub HTTP handler stands in for the provider).</summary>
public sealed class DirectOidcBackChannelTests : IDisposable
{
    private readonly StubProvider http = new();

    public void Dispose() => http.Dispose();

    [Fact]
    public async Task The_token_request_is_a_form_post_to_the_token_endpoint_asking_for_json()
    {
        http.Json(TestIssuer.TokenEndpoint, "{\"id_token\":\"x\"}");

        var response = await Channel().RedeemCodeAsync([new("code", "c1")], CancellationToken.None);

        Assert.Equal(OidcBackChannelOutcome.Answered, response.Outcome);
        Assert.Equal((int)HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("{\"id_token\":\"x\"}", response.Body);
        var request = Assert.Single(http.Requests);
        Assert.Equal(HttpMethod.Post, request.Method);
        Assert.Contains(request.Headers.Accept, accept => accept.MediaType == "application/json");
        Assert.Equal(new Dictionary<string, string> { ["code"] = "c1" }, http.LastForm);
    }

    [Fact]
    public async Task A_provider_refusal_is_answered_with_its_status()
    {
        http.Json(TestIssuer.TokenEndpoint, "{\"error\":\"invalid_grant\"}", HttpStatusCode.BadRequest);

        var response = await Channel().RedeemCodeAsync([new("code", "c1")], CancellationToken.None);

        Assert.Equal(OidcBackChannelOutcome.Answered, response.Outcome);
        Assert.Equal((int)HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task A_transport_failure_is_unavailable_with_the_exception_type_as_reason()
    {
        http.Throws(TestIssuer.JwksUri, new HttpRequestException("connection refused"));

        var response = await Channel().GetJwksAsync(CancellationToken.None);

        Assert.Equal(OidcBackChannelOutcome.Unavailable, response.Outcome);
        Assert.Equal(nameof(HttpRequestException), response.FailureReason);
        Assert.Null(response.Body);
    }

    [Fact]
    public async Task The_jwks_is_a_get_of_the_jwks_uri()
    {
        http.Json(TestIssuer.JwksUri, "{\"keys\":[]}");

        var response = await Channel().GetJwksAsync(CancellationToken.None);

        Assert.Equal("{\"keys\":[]}", response.Body);
        Assert.Equal(HttpMethod.Get, Assert.Single(http.Requests).Method);
    }

    [Fact]
    public async Task Cancellation_propagates()
    {
        using var cancelled = new CancellationTokenSource();
        await cancelled.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Channel().GetJwksAsync(cancelled.Token));
    }

    private DirectOidcBackChannel Channel() => new(new StubClientFactory(http), Options.Create(TestIssuer.Options()));
}

/// <summary>What <c>AddCodelabOidc</c> registers for the back channel, and the start-up mismatch check.</summary>
public sealed class OidcBackChannelRegistrationTests
{
    private const string Section = OidcOptions.SectionName;

    [Fact]
    public void The_direct_back_channel_is_the_default()
    {
        using var provider = Provider(backChannel: null, registered: null);

        Assert.IsType<DirectOidcBackChannel>(provider.GetRequiredService<IOidcBackChannel>());
        Assert.NotNull(provider.GetRequiredService<IOptions<OidcOptions>>().Value);
    }

    [Fact]
    public void A_host_relay_registered_first_is_kept_and_feeds_the_jwks_source()
    {
        var relay = new ScriptedBackChannel();
        using var provider = Provider(backChannel: "2", registered: relay);

        Assert.Same(relay, provider.GetRequiredService<IOidcBackChannel>());
        Assert.Equal(OidcBackChannel.GatewayRelay, provider.GetRequiredService<IOptions<OidcOptions>>().Value.BackChannel);
        Assert.IsType<JwksSigningKeySource>(provider.GetRequiredService<IIdTokenSigningKeySource>());
    }

    [Fact]
    public void Relay_mode_without_a_relay_registered_fails_the_start()
    {
        using var provider = Provider(backChannel: "2", registered: null);

        var failure = Assert.Throws<OptionsValidationException>(() => provider.GetRequiredService<IOptions<OidcOptions>>().Value);

        Assert.Contains($"{Section}:{nameof(OidcOptions.BackChannel)} is 2 (GatewayRelay), but the registered back channel is 1 (Direct)", failure.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void A_relay_registered_for_a_direct_deployment_fails_the_start()
    {
        using var provider = Provider(backChannel: "1", registered: new ScriptedBackChannel());

        Assert.Throws<OptionsValidationException>(() => provider.GetRequiredService<IOptions<OidcOptions>>().Value);
    }

    private static ServiceProvider Provider(string? backChannel, IOidcBackChannel? registered)
    {
        var values = new Dictionary<string, string?>
        {
            [$"{Section}:Enabled"] = "true",
            [$"{Section}:Authority"] = TestIssuer.Authority,
            [$"{Section}:ClientId"] = TestIssuer.ClientId,
            [$"{Section}:RedirectUri"] = TestIssuer.RedirectUri,
            [$"{Section}:AuthorizationEndpoint"] = TestIssuer.AuthorizationEndpoint,
            [$"{Section}:TokenEndpoint"] = TestIssuer.TokenEndpoint,
            [$"{Section}:EndSessionEndpoint"] = TestIssuer.EndSessionEndpoint,
            [$"{Section}:JwksUri"] = TestIssuer.JwksUri,
        };
        if (backChannel is not null)
        {
            values[$"{Section}:{nameof(OidcOptions.BackChannel)}"] = backChannel;
        }

        var services = new ServiceCollection();
        services.AddSingleton<IHostEnvironment>(new TestHostEnvironment(Environments.Production));
        services.AddLogging();
        if (registered is not null)
        {
            services.AddSingleton(registered);
        }

        services.AddCodelabOidc(new ConfigurationBuilder().AddInMemoryCollection(values).Build());
        return services.BuildServiceProvider();
    }
}
