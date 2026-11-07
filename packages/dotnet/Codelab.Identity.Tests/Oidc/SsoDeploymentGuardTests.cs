using Codelab.Identity.Oidc;
using Codelab.Identity.Tests.TestSupport;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

namespace Codelab.Identity.Tests.Oidc;

public sealed class SsoDeploymentGuardTests
{
    private const string Section = OidcOptions.SectionName;

    private static OidcOptions Valid(Action<OidcOptions>? change = null)
    {
        var options = TestIssuer.Options();
        change?.Invoke(options);
        return options;
    }

    private static string Single(OidcOptions options, bool isDevelopment = false) =>
        Assert.Single(SsoDeploymentGuard.Check(options, isDevelopment));

    [Fact]
    public void A_disabled_section_has_nothing_to_refuse() =>
        Assert.Empty(SsoDeploymentGuard.Check(new OidcOptions(), isDevelopment: false));

    [Fact]
    public void A_complete_https_section_starts_in_production() =>
        Assert.Empty(SsoDeploymentGuard.Check(Valid(), isDevelopment: false));

    [Theory]
    [InlineData(nameof(OidcOptions.Authority))]
    [InlineData(nameof(OidcOptions.RedirectUri))]
    [InlineData(nameof(OidcOptions.AuthorizationEndpoint))]
    [InlineData(nameof(OidcOptions.TokenEndpoint))]
    [InlineData(nameof(OidcOptions.EndSessionEndpoint))]
    [InlineData(nameof(OidcOptions.JwksUri))]
    public void A_missing_or_relative_uri_is_refused(string key)
    {
        var options = Valid();
        typeof(OidcOptions).GetProperty(key)!.SetValue(options, "/relative");

        Assert.Equal($"{Section}:{key} must be an absolute http(s) URI", Single(options));
    }

    [Theory]
    [InlineData(nameof(OidcOptions.Authority))]
    [InlineData(nameof(OidcOptions.TokenEndpoint))]
    [InlineData(nameof(OidcOptions.JwksUri))]
    [InlineData(nameof(OidcOptions.PostLogoutRedirectUri))]
    public void Plain_http_is_refused_outside_development(string key)
    {
        var options = Valid();
        typeof(OidcOptions).GetProperty(key)!.SetValue(options, "http://localhost:5190/x");

        Assert.Equal($"{Section}:{key} must use https outside Development", Single(options));
        Assert.Empty(SsoDeploymentGuard.Check(options, isDevelopment: true));
    }

    [Fact]
    public void A_missing_client_id_is_refused() =>
        Assert.Equal($"{Section}:ClientId must be set", Single(Valid(o => o.ClientId = "")));

    [Fact]
    public void A_missing_client_secret_is_allowed_for_a_public_client() =>
        Assert.Empty(SsoDeploymentGuard.Check(Valid(o => o.ClientSecret = ""), isDevelopment: false));

    [Fact]
    public void Scopes_without_openid_are_refused() =>
        Assert.Equal($"{Section}:Scopes must include openid", Single(Valid(o => o.Scopes = ["profile", "email"])));

    [Theory]
    [InlineData("none")]
    [InlineData(SecurityAlgorithms.HmacSha256)]
    [InlineData("HS512")]
    [InlineData("rs256")]
    public void A_non_asymmetric_algorithm_is_refused(string algorithm) =>
        Assert.StartsWith($"{Section}:AllowedAlgorithms may hold only asymmetric algorithms", Single(Valid(o => o.AllowedAlgorithms = [SecurityAlgorithms.RsaSha256, algorithm])), StringComparison.Ordinal);

    [Fact]
    public void Pinned_mode_needs_a_key_and_no_jwks_uri()
    {
        var options = Valid(o => { o.SigningKeyMode = SigningKeyMode.Pinned; o.JwksUri = ""; });

        Assert.Equal($"{Section}:PinnedKeys must hold at least one public key in Pinned mode", Single(options));
    }

    [Fact]
    public void An_unusable_pinned_key_is_refused_by_index()
    {
        var options = Valid(o => { o.SigningKeyMode = SigningKeyMode.Pinned; o.PinnedKeys = [new OidcPinnedKey { PublicKey = "garbage" }]; });

        Assert.StartsWith($"{Section}:PinnedKeys:0 is unusable", Single(options), StringComparison.Ordinal);
    }

    [Fact]
    public void A_parseable_pinned_key_starts()
    {
        using var issuer = new TestIssuer(TimeProvider.System);
        var options = Valid(o => { o.SigningKeyMode = SigningKeyMode.Pinned; o.PinnedKeys = [new OidcPinnedKey { PublicKey = issuer.PublicKeyPem() }]; });

        Assert.Empty(SsoDeploymentGuard.Check(options, isDevelopment: false));
    }

    [Fact]
    public void An_undefined_key_mode_is_refused() =>
        Assert.StartsWith($"{Section}:SigningKeyMode must be one of", Single(Valid(o => o.SigningKeyMode = (SigningKeyMode)7)), StringComparison.Ordinal);

    [Theory]
    [InlineData(-1, 15)]
    [InlineData(SsoDeploymentGuard.MaxClockSkewSeconds + 1, 15)]
    [InlineData(120, 0)]
    [InlineData(120, SsoDeploymentGuard.MaxBackchannelTimeoutSeconds + 1)]
    public void Clock_skew_and_timeout_are_bounded(int skew, int timeout) =>
        Assert.Single(SsoDeploymentGuard.Check(Valid(o => { o.ClockSkewSeconds = skew; o.BackchannelTimeoutSeconds = timeout; }), isDevelopment: false));

    [Fact]
    public void An_undefined_match_field_and_a_blank_claim_name_are_refused()
    {
        var problems = SsoDeploymentGuard.Check(Valid(o => { o.MatchBy = (LoginMatchField)0; o.Claims.LoginName = " "; }), isDevelopment: false);

        Assert.Equal(2, problems.Count);
        Assert.Contains(problems, problem => problem.StartsWith($"{Section}:MatchBy", StringComparison.Ordinal));
        Assert.Contains($"{Section}:Claims:LoginName must name a claim", problems);
    }

    [Fact]
    public void Messages_never_carry_the_client_secret()
    {
        var secretValue = Guid.NewGuid().ToString("N");
        var problems = SsoDeploymentGuard.Check(Valid(o => { o.ClientSecret = secretValue; o.ClientId = ""; o.Authority = "nope"; }), isDevelopment: false);

        Assert.Equal(2, problems.Count);
        Assert.All(problems, problem => Assert.DoesNotContain(secretValue, problem, StringComparison.Ordinal));
    }

    [Fact]
    public void Enum_members_keep_their_stored_values()
    {
        Assert.Equal([1, 2], Enum.GetValues<SigningKeyMode>().Select(value => (int)value));
        Assert.Equal([1, 2, 3], Enum.GetValues<OidcSignInStatus>().Select(value => (int)value));
        Assert.Equal([1, 2, 3, 4, 5, 6], Enum.GetValues<OidcFailureReason>().Select(value => (int)value));
        Assert.Equal([1, 2, 3], Enum.GetValues<IdTokenValidationStatus>().Select(value => (int)value));
    }

    [Fact]
    public void The_registration_fails_the_host_on_start_when_enabled_without_its_keys()
    {
        using var provider = Provider(Environments.Production, new Dictionary<string, string?> { [$"{Section}:Enabled"] = "true" });

        var failure = Assert.Throws<OptionsValidationException>(() => provider.GetRequiredService<IOptions<OidcOptions>>().Value);
        Assert.Contains($"{Section}:Authority", failure.Message, StringComparison.Ordinal);
        Assert.Contains($"{Section}:ClientId", failure.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void The_registration_binds_lists_and_resolves_the_configured_key_source()
    {
        using var issuer = new TestIssuer(TimeProvider.System);
        using var provider = Provider(Environments.Production, Configured(new Dictionary<string, string?>
        {
            [$"{Section}:SigningKeyMode"] = "Pinned",
            [$"{Section}:PinnedKeys:0:Kid"] = "k1",
            [$"{Section}:PinnedKeys:0:PublicKey"] = issuer.PublicKeyPem(),
            [$"{Section}:Scopes:0"] = "openid",
            [$"{Section}:AllowedAlgorithms:0"] = "RS384",
            [$"{Section}:MatchBy"] = "LoginName",
        }));

        var options = provider.GetRequiredService<IOptions<OidcOptions>>().Value;
        Assert.Equal(["openid"], options.EffectiveScopes);
        Assert.Equal(["RS384"], options.EffectiveAllowedAlgorithms);
        Assert.Equal(LoginMatchField.LoginName, options.MatchBy);
        Assert.IsType<PinnedSigningKeySource>(provider.GetRequiredService<IIdTokenSigningKeySource>());
        Assert.NotNull(provider.GetRequiredService<IOidcIdentityProvider>());
    }

    [Fact]
    public void A_declared_no_egress_deployment_refuses_the_direct_back_channel_outside_development()
    {
        var options = Valid(change => change.NoEgress = true);

        Assert.Equal(
            $"{Section}:{nameof(OidcOptions.BackChannel)} must be 2 (GatewayRelay) outside Development: {nameof(OidcOptions.NoEgress)} declares that this backend cannot reach the provider",
            Single(options));
        Assert.Empty(SsoDeploymentGuard.Check(options, isDevelopment: true));
    }

    [Fact]
    public void A_no_egress_deployment_on_the_gateway_relay_starts_in_production() =>
        Assert.Empty(SsoDeploymentGuard.Check(Valid(change => (change.NoEgress, change.BackChannel) = (true, OidcBackChannel.GatewayRelay)), isDevelopment: false));

    [Fact]
    public void The_gateway_relay_needs_no_token_endpoint_or_jwks_uri_of_its_own()
    {
        var options = Valid(change => (change.BackChannel, change.TokenEndpoint, change.JwksUri) = (OidcBackChannel.GatewayRelay, string.Empty, string.Empty));

        Assert.Empty(SsoDeploymentGuard.Check(options, isDevelopment: false));
    }

    [Fact]
    public void The_gateway_relay_keeps_the_browser_endpoints_required()
    {
        var options = Valid(change => (change.BackChannel, change.EndSessionEndpoint) = (OidcBackChannel.GatewayRelay, string.Empty));

        Assert.Equal($"{Section}:{nameof(OidcOptions.EndSessionEndpoint)} must be an absolute http(s) URI", Single(options));
    }

    [Fact]
    public void An_undefined_back_channel_is_refused() =>
        Assert.Equal(
            $"{Section}:{nameof(OidcOptions.BackChannel)} must be 1 (Direct) or 2 (GatewayRelay)",
            Single(Valid(change => change.BackChannel = (OidcBackChannel)9)));

    [Fact]
    public void The_named_client_carries_the_back_channel_timeout_and_size_cap()
    {
        using var provider = Provider(Environments.Production, Configured(new Dictionary<string, string?> { [$"{Section}:BackchannelTimeoutSeconds"] = "9" }));

        var client = provider.GetRequiredService<IHttpClientFactory>().CreateClient(OidcHttpClient.Name);

        Assert.Equal(TimeSpan.FromSeconds(9), client.Timeout);
        Assert.Equal(OidcHttpClient.MaxResponseBytes, client.MaxResponseContentBufferSize);
        Assert.IsType<JwksSigningKeySource>(provider.GetRequiredService<IIdTokenSigningKeySource>());
    }

    private static Dictionary<string, string?> Configured(Dictionary<string, string?> extra)
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
        foreach (var (key, value) in extra)
        {
            values[key] = value;
        }

        return values;
    }

    private static ServiceProvider Provider(string environment, Dictionary<string, string?> values)
    {
        var services = new ServiceCollection();
        services.AddSingleton<IHostEnvironment>(new TestHostEnvironment(environment));
        services.AddLogging();
        services.AddCodelabOidc(new ConfigurationBuilder().AddInMemoryCollection(values).Build());
        return services.BuildServiceProvider();
    }
}
