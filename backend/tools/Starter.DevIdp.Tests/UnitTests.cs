using System.Text;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Time.Testing;
using Starter.DevIdp.Authorize;
using Starter.DevIdp.Configuration;
using Starter.DevIdp.Grants;
using Starter.DevIdp.Hosting;
using Starter.DevIdp.Protocol;
using Starter.DevIdp.Token;

namespace Starter.DevIdp.Tests;

public sealed class PkceTests
{
    // The RFC 7636 appendix B vector is on the secret scanner's known-value list, so the expected challenge is computed
    // here independently: BASE64URL (RFC 4648 section 5, no padding) of SHA-256 over the ASCII verifier.
    private static readonly string Verifier = new('v', 43);

    [Fact]
    public void The_challenge_is_unpadded_base64url_of_the_sha256_of_the_verifier() =>
        Assert.Equal(IndependentChallenge(Verifier), Pkce.ChallengeOf(Verifier));

    [Fact]
    public void The_matching_verifier_verifies() => Assert.True(Pkce.Verify(Verifier, IndependentChallenge(Verifier)));

    [Theory]
    [InlineData(null)]
    [InlineData("wwwwwwwwwwwwwwwwwwwwwwwwwwwwwwwwwwwwwwwwwww")]
    [InlineData("short")]
    public void Another_or_malformed_verifier_fails(string? verifier) => Assert.False(Pkce.Verify(verifier, IndependentChallenge(Verifier)));

    [Theory]
    [InlineData(42, 'a', false)]
    [InlineData(43, 'a', true)]
    [InlineData(128, '~', true)]
    [InlineData(129, 'a', false)]
    [InlineData(43, '+', false)]
    [InlineData(43, '=', false)]
    public void Well_formed_means_43_to_128_unreserved_characters(int length, char character, bool expected) =>
        Assert.Equal(expected, Pkce.IsWellFormed(new string(character, length)));

    private static string IndependentChallenge(string verifier) =>
        Convert.ToBase64String(System.Security.Cryptography.SHA256.HashData(Encoding.ASCII.GetBytes(verifier)))
            .TrimEnd('=').Replace('+', '-').Replace('/', '_');
}

public sealed class ExpiringStoreTests
{
    private readonly FakeTimeProvider _clock = new();

    [Fact]
    public void Take_returns_the_value_once()
    {
        var store = new ExpiringStore<string>(_clock);
        var handle = store.Add("value", TimeSpan.FromSeconds(10));

        Assert.Equal("value", store.Take(handle));
        Assert.Null(store.Take(handle));
    }

    [Fact]
    public void Find_reads_without_removing_until_the_expiry()
    {
        var store = new ExpiringStore<string>(_clock);
        var handle = store.Add("value", TimeSpan.FromSeconds(10));

        Assert.Equal("value", store.Find(handle));
        _clock.Advance(TimeSpan.FromSeconds(9));
        Assert.Equal("value", store.Find(handle));
        _clock.Advance(TimeSpan.FromSeconds(1));
        Assert.Null(store.Find(handle));
    }

    [Fact]
    public void An_expired_value_cannot_be_taken_and_is_removed()
    {
        var store = new ExpiringStore<string>(_clock);
        var handle = store.Add("value", TimeSpan.FromSeconds(10));

        _clock.Advance(TimeSpan.FromSeconds(10));

        Assert.Null(store.Take(handle));
        Assert.Equal(0, store.Count);
    }

    [Fact]
    public void Adding_sweeps_expired_entries()
    {
        var store = new ExpiringStore<string>(_clock);
        store.Add("old", TimeSpan.FromSeconds(1));
        _clock.Advance(TimeSpan.FromSeconds(2));

        store.Add("new", TimeSpan.FromSeconds(1));

        Assert.Equal(1, store.Count);
    }

    [Fact]
    public void Handles_are_unique_and_unknown_handles_read_as_null()
    {
        var store = new ExpiringStore<string>(_clock);

        var handles = Enumerable.Range(0, 50).Select(_ => store.Add("v", TimeSpan.FromSeconds(1))).ToHashSet(StringComparer.Ordinal);

        Assert.Equal(50, handles.Count);
        Assert.All(handles, handle => Assert.True(handle.Length >= 43));
        Assert.Null(store.Find("unknown"));
        Assert.Null(store.Take(null));
    }
}

public sealed class DevIdpOptionsValidatorTests
{
    [Fact]
    public void The_embedded_development_settings_are_valid()
    {
        var options = EmbeddedOptions();

        Assert.True(new DevIdpOptionsValidator().Validate(name: null, options).Succeeded);
    }

    [Theory]
    [InlineData("localhost:5190")]
    [InlineData("ftp://localhost:5190")]
    [InlineData("http://localhost:5190/idp")]
    [InlineData("http://localhost:5190/?x=1")]
    [InlineData("")]
    public void A_bad_issuer_fails(string issuer)
    {
        var options = EmbeddedOptions();
        options.Issuer = issuer;

        AssertFails(options, "Issuer");
    }

    [Fact]
    public void Lifetimes_below_one_second_fail()
    {
        var options = EmbeddedOptions();
        options.CodeLifetimeSeconds = 0;
        options.TokenLifetimeSeconds = -1;

        AssertFails(options, "CodeLifetimeSeconds", "TokenLifetimeSeconds");
    }

    [Fact]
    public void A_client_without_an_id_a_redirect_or_with_a_relative_uri_fails()
    {
        var options = EmbeddedOptions();
        var client = new DevIdpClient();
        client.PostLogoutRedirectUris.Add("/relative");
        options.Clients.Add(client);

        AssertFails(options, "without a ClientId", "no RedirectUris", "not absolute");
    }

    [Fact]
    public void Duplicates_and_empty_lists_fail()
    {
        var options = new DevIdpOptions();
        AssertFails(options, "at least one client", "at least one fixture user");

        var duplicated = EmbeddedOptions();
        duplicated.Clients.Add(duplicated.Clients[0]);
        duplicated.Users.Add(duplicated.Users[0]);
        duplicated.Users.Add(new DevIdpUser());
        AssertFails(duplicated, "registers 'starter-web' more than once", "more than once", "without a Subject");
    }

    internal static DevIdpOptions EmbeddedOptions()
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(DevIdpEmbeddedSettings.Read()).Build();
        var options = new DevIdpOptions();
        configuration.GetSection(DevIdpOptions.SectionName).Bind(options);
        return options;
    }

    private static void AssertFails(DevIdpOptions options, params string[] fragments)
    {
        var result = new DevIdpOptionsValidator().Validate(name: null, options);

        Assert.True(result.Failed);
        foreach (var fragment in fragments)
        {
            Assert.Contains(result.Failures!, failure => failure.Contains(fragment, StringComparison.Ordinal));
        }
    }
}

public sealed class EmbeddedSettingsTests
{
    [Fact]
    public void The_development_settings_register_the_starter_client_and_neutral_fixture_users()
    {
        var options = DevIdpOptionsValidatorTests.EmbeddedOptions();

        var client = Assert.Single(options.Clients);
        Assert.Equal("starter-web", client.ClientId);
        Assert.False(client.IsConfidential);
        Assert.Equal(["http://localhost:5180/api/auth/sso/signin-oidc"], client.RedirectUris);
        Assert.Equal(["http://localhost:4280/login"], client.PostLogoutRedirectUris);
        Assert.Equal(DevIdpOptions.DefaultIssuer, options.Issuer);
        Assert.InRange(options.Users.Count, 2, 3);
        Assert.All(options.Users, user =>
        {
            Assert.EndsWith("@example.test", user.Email, StringComparison.Ordinal);
            Assert.EndsWith("@example.test", user.Upn, StringComparison.Ordinal);
            Assert.False(string.IsNullOrEmpty(user.LoginName));
        });
        Assert.Contains(options.Users, user => user.LoginName == "dev.viewer");
    }
}

public sealed class DevIdpUserTests
{
    private readonly DevIdpUser _user = new() { Subject = "Sub-1", LoginName = "dev.user", Email = "dev.user@example.test", Upn = "dev.user@corp.example.test" };

    [Theory]
    [InlineData("Sub-1", true)]
    [InlineData("sub-1", false)]
    [InlineData("DEV.USER", true)]
    [InlineData("Dev.User@Example.Test", true)]
    [InlineData("dev.user@corp.example.test", true)]
    [InlineData("other", false)]
    public void A_hint_matches_the_exact_subject_or_the_login_name_email_or_upn_ignoring_case(string hint, bool expected) =>
        Assert.Equal(expected, _user.Matches(hint));
}

public sealed class ClientAuthenticatorTests
{
    // Generated per run; the suffix makes sure the form-url-encoding of client_secret_basic is exercised.
    private static readonly string Secret = Convert.ToBase64String(System.Security.Cryptography.RandomNumberGenerator.GetBytes(24)) + ":+ /";
    private readonly DevIdpOptions _options = new();

    public ClientAuthenticatorTests()
    {
        _options.Clients.Add(new DevIdpClient { ClientId = "confidential", ClientSecret = Secret });
        _options.Clients.Add(new DevIdpClient { ClientId = "public" });
    }

    [Fact]
    public void Client_secret_post_with_the_right_secret_authenticates() =>
        Assert.Equal("confidential", Authenticate(Form(("client_id", "confidential"), ("client_secret", Secret)), header: null)?.ClientId);

    [Fact]
    public void Client_secret_basic_decodes_form_url_encoded_credentials() =>
        Assert.Equal("confidential", Authenticate(Form(), Basic("confidential", Uri.EscapeDataString(Secret)))?.ClientId);

    [Theory]
    [InlineData("Basic !!!")]
    [InlineData("Basic bm9jb2xvbg==")]
    [InlineData("Bearer abc")]
    [InlineData("garbage")]
    public void A_malformed_authorization_header_fails(string header) => Assert.Null(Authenticate(Form(), header));

    [Fact]
    public void A_form_client_id_that_differs_from_the_basic_one_fails() =>
        Assert.Null(Authenticate(Form(("client_id", "public")), Basic("confidential", Uri.EscapeDataString(Secret))));

    [Fact]
    public void A_wrong_or_missing_secret_fails()
    {
        Assert.Null(Authenticate(Form(("client_id", "confidential"), ("client_secret", "nope")), header: null));
        Assert.Null(Authenticate(Form(("client_id", "confidential")), header: null));
    }

    [Fact]
    public void A_public_client_authenticates_by_id_and_ignores_a_presented_secret()
    {
        Assert.Equal("public", Authenticate(Form(("client_id", "public")), header: null)?.ClientId);
        Assert.Equal("public", Authenticate(Form(("client_id", "public"), ("client_secret", "anything")), header: null)?.ClientId);
    }

    [Fact]
    public void An_unknown_or_missing_client_fails()
    {
        Assert.Null(Authenticate(Form(("client_id", "unknown")), header: null));
        Assert.Null(Authenticate(Form(), header: null));
    }

    private DevIdpClient? Authenticate(OidcParameters form, string? header) => ClientAuthenticator.Authenticate(form, header, _options);

    private static OidcParameters Form(params (string Name, string Value)[] pairs) =>
        OidcParameters.From(pairs.ToDictionary(pair => pair.Name, pair => pair.Value));

    private static string Basic(string clientId, string secret) =>
        "Basic " + Convert.ToBase64String(Encoding.UTF8.GetBytes($"{clientId}:{secret}"));
}

public sealed class AuthorizationRequestValidatorTests
{
    [Fact]
    public void A_valid_request_keeps_state_nonce_and_hint_and_its_form_fields_round_trip()
    {
        var options = DevIdpOptionsValidatorTests.EmbeddedOptions();
        var parameters = new Dictionary<string, string>
        {
            ["client_id"] = "starter-web",
            ["redirect_uri"] = "http://localhost:5180/api/auth/sso/signin-oidc",
            ["response_type"] = "code",
            ["scope"] = "openid email",
            ["state"] = "s",
            ["nonce"] = "n",
            ["code_challenge"] = Pkce.ChallengeOf(new string('v', 43)),
            ["code_challenge_method"] = "S256",
            ["login_hint"] = "dev.viewer",
        };

        var validation = AuthorizationRequestValidator.Validate(OidcParameters.From(parameters), options);

        var request = Assert.IsType<AuthorizationRequest>(validation.Request);
        Assert.Equal("n", request.Nonce);
        Assert.Equal("dev.viewer", request.LoginHint);
        var fields = request.ToFormFields().ToDictionary(field => field.Key, field => field.Value);
        parameters.Remove("login_hint");
        Assert.Equal(parameters.OrderBy(pair => pair.Key, StringComparer.Ordinal), fields.OrderBy(pair => pair.Key, StringComparer.Ordinal));
    }
}

public sealed class EnvironmentGuardTests
{
    [Theory]
    [InlineData("Production")]
    [InlineData("Staging")]
    [InlineData("Test")]
    public void Any_environment_but_development_is_refused(string environmentName)
    {
        var refused = Assert.Throws<DevIdpEnvironmentException>(() =>
            DevIdpEnvironmentGuard.EnsureDevelopment(new FixedEnvironment(environmentName)));

        Assert.Equal(environmentName, refused.EnvironmentName);
    }

    [Fact]
    public void Development_is_allowed() => DevIdpEnvironmentGuard.EnsureDevelopment(new FixedEnvironment(Environments.Development));

    [Theory]
    [InlineData("Production")]
    [InlineData("Staging")]
    public void The_standalone_host_refuses_to_build_outside_development(string environmentName) =>
        Assert.Throws<DevIdpEnvironmentException>(() => DevIdpApp.Build(["--environment", environmentName]));

    [Fact]
    public async Task The_standalone_host_builds_in_development()
    {
        await using var app = DevIdpApp.Build(["--environment", Environments.Development, "--urls", "http://127.0.0.1:0"]);

        Assert.True(app.Environment.IsDevelopment());
    }

    [Fact]
    public async Task Mapping_the_endpoints_into_another_host_outside_development_is_refused()
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = Environments.Production });
        builder.Services.AddDevIdp(builder.Configuration);
        await using var app = builder.Build();

        Assert.Throws<DevIdpEnvironmentException>(() => app.MapDevIdp());
    }

    private sealed class FixedEnvironment(string environmentName) : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = environmentName;

        public string ApplicationName { get; set; } = "Starter.DevIdp.Tests";

        public string ContentRootPath { get; set; } = AppContext.BaseDirectory;

        public Microsoft.Extensions.FileProviders.IFileProvider ContentRootFileProvider { get; set; } =
            new Microsoft.Extensions.FileProviders.NullFileProvider();
    }
}
