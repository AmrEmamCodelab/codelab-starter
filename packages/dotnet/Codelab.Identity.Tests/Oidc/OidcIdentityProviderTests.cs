using System.Net;
using Codelab.Identity.Oidc;
using Codelab.Identity.Tests.TestSupport;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;

namespace Codelab.Identity.Tests.Oidc;

public sealed class OidcIdentityProviderTests : IDisposable
{
    private const string Code = "authorization-code-1";
    private const string State = "state-1";
    private const string Verifier = "verifier-1";

    private readonly FakeTimeProvider clock = new(new DateTimeOffset(2026, 11, 5, 9, 0, 0, TimeSpan.Zero));
    private readonly TestIssuer issuer;
    private readonly StubProvider http = new();

    public OidcIdentityProviderTests()
    {
        issuer = new TestIssuer(clock);
        http.Json(TestIssuer.JwksUri, issuer.Jwks());
    }

    public void Dispose()
    {
        issuer.Dispose();
        http.Dispose();
    }

    private static OidcPendingAuthorization Pending => new(State, Verifier, TestIssuer.Nonce);

    private OidcIdentityProvider Provider(OidcOptions? options = null)
    {
        var settings = Options.Create(options ?? TestIssuer.Options());
        var clients = new StubClientFactory(http);
        IIdTokenSigningKeySource keys = settings.Value.SigningKeyMode == SigningKeyMode.Pinned
            ? new PinnedSigningKeySource(settings)
            : new JwksSigningKeySource(clients, settings, clock, NullLogger<JwksSigningKeySource>.Instance);
        var validator = new OidcIdTokenValidator(settings, keys, clock, NullLogger<OidcIdTokenValidator>.Instance);
        return new OidcIdentityProvider(settings, validator, new DirectOidcBackChannel(clients, settings), NullLogger<OidcIdentityProvider>.Instance);
    }

    private Task<OidcSignInResult> Complete(OidcIdentityProvider? provider = null, OidcCallback? callback = null) =>
        (provider ?? Provider()).CompleteAsync(callback ?? new OidcCallback(Code, State), Pending, CancellationToken.None);

    [Fact]
    public async Task A_valid_callback_redeems_the_code_and_maps_the_identity()
    {
        var token = issuer.Token();
        http.Json(TestIssuer.TokenEndpoint, StubProvider.TokenResponse(token));

        var result = await Complete();

        Assert.Equal(OidcSignInStatus.Succeeded, result.Status);
        Assert.Equal(new OidcIdentity(TestIssuer.Subject, "user.one@example.org", "user.one", "user.one@example.org", "User One", token), result.Identity);
    }

    [Fact]
    public async Task The_token_request_carries_the_code_verifier_redirect_and_client_credentials()
    {
        var options = TestIssuer.Options();
        options.ClientSecret = Guid.NewGuid().ToString("N");
        http.Json(TestIssuer.TokenEndpoint, StubProvider.TokenResponse(issuer.Token()));

        await Complete(Provider(options));

        Assert.Equal(
            new Dictionary<string, string>
            {
                ["grant_type"] = "authorization_code",
                ["code"] = Code,
                ["redirect_uri"] = TestIssuer.RedirectUri,
                ["client_id"] = TestIssuer.ClientId,
                ["code_verifier"] = Verifier,
                ["client_secret"] = options.ClientSecret,
            },
            http.LastForm);
    }

    [Fact]
    public async Task A_public_client_sends_no_client_secret()
    {
        http.Json(TestIssuer.TokenEndpoint, StubProvider.TokenResponse(issuer.Token()));

        await Complete();

        Assert.DoesNotContain("client_secret", http.LastForm!.Keys);
    }

    [Theory]
    [InlineData("state-2")]
    [InlineData("")]
    public async Task A_state_mismatch_is_rejected_without_calling_the_provider(string state)
    {
        var result = await Complete(callback: new OidcCallback(Code, state));

        Assert.Equal(OidcSignInResult.Rejected(OidcFailureReason.StateMismatch), result);
        Assert.Empty(http.Requests);
    }

    [Fact]
    public async Task A_missing_code_is_rejected_without_calling_the_provider()
    {
        Assert.Equal(OidcSignInResult.Rejected(OidcFailureReason.MissingCode), await Complete(callback: new OidcCallback(" ", State)));
        Assert.Empty(http.Requests);
    }

    [Theory]
    [InlineData(HttpStatusCode.BadRequest, OidcSignInStatus.Rejected)]
    [InlineData(HttpStatusCode.Unauthorized, OidcSignInStatus.Rejected)]
    [InlineData(HttpStatusCode.InternalServerError, OidcSignInStatus.Unavailable)]
    [InlineData(HttpStatusCode.BadGateway, OidcSignInStatus.Unavailable)]
    public async Task A_token_endpoint_refusal_maps_by_status(HttpStatusCode status, OidcSignInStatus expected)
    {
        http.Json(TestIssuer.TokenEndpoint, "{\"error\":\"invalid_grant\"}", status);

        Assert.Equal(expected, (await Complete()).Status);
    }

    [Fact]
    public async Task An_unreachable_token_endpoint_is_unavailable()
    {
        http.Throws(TestIssuer.TokenEndpoint, new HttpRequestException("no route"));

        Assert.Equal(OidcSignInResult.Unavailable(), await Complete());
    }

    [Fact]
    public async Task A_client_timeout_is_unavailable()
    {
        http.Throws(TestIssuer.TokenEndpoint, new TaskCanceledException("timeout"));

        Assert.Equal(OidcSignInStatus.Unavailable, (await Complete()).Status);
    }

    [Theory]
    [InlineData("{\"access_token\":\"at\"}")]
    [InlineData("not json")]
    [InlineData("{\"id_token\":42}")]
    public async Task A_token_response_without_an_id_token_is_rejected(string body)
    {
        http.Json(TestIssuer.TokenEndpoint, body);

        Assert.Equal(OidcSignInResult.Rejected(OidcFailureReason.TokenResponseInvalid), await Complete());
    }

    [Fact]
    public async Task An_invalid_id_token_is_rejected()
    {
        http.Json(TestIssuer.TokenEndpoint, StubProvider.TokenResponse(issuer.Token(claims => claims["nonce"] = "replayed")));

        Assert.Equal(OidcSignInResult.Rejected(OidcFailureReason.IdTokenInvalid), await Complete());
    }

    [Fact]
    public async Task An_unsigned_id_token_from_the_token_endpoint_is_rejected()
    {
        http.Json(TestIssuer.TokenEndpoint, StubProvider.TokenResponse(issuer.Unsigned()));

        Assert.Equal(OidcSignInResult.Rejected(OidcFailureReason.IdTokenInvalid), await Complete());
    }

    [Fact]
    public async Task Unobtainable_signing_keys_are_unavailable()
    {
        http.Json(TestIssuer.TokenEndpoint, StubProvider.TokenResponse(issuer.Token()));
        http.Throws(TestIssuer.JwksUri, new HttpRequestException("down"));

        Assert.Equal(OidcSignInResult.Unavailable(), await Complete());
    }

    [Fact]
    public async Task An_unverified_email_is_not_released()
    {
        http.Json(TestIssuer.TokenEndpoint, StubProvider.TokenResponse(issuer.Token(claims => claims["email_verified"] = false)));

        var identity = (await Complete()).Identity!;

        Assert.Null(identity.Email);
        Assert.Null(LoginMatch.ValueOf(identity, LoginMatchField.Email));
    }

    [Fact]
    public async Task The_email_is_released_unverified_when_the_deployment_says_so()
    {
        var options = TestIssuer.Options();
        options.RequireVerifiedEmail = false;
        http.Json(TestIssuer.TokenEndpoint, StubProvider.TokenResponse(issuer.Token(claims => claims["email_verified"] = null)));

        Assert.Equal("user.one@example.org", (await Complete(Provider(options))).Identity!.Email);
    }

    [Fact]
    public async Task Provider_specific_claim_names_are_configuration()
    {
        var options = TestIssuer.Options();
        options.Claims.LoginName = "loginName";
        http.Json(TestIssuer.TokenEndpoint, StubProvider.TokenResponse(issuer.Token(claims => { claims["loginName"] = "dir.user"; claims["email_verified"] = "true"; })));

        var identity = (await Complete(Provider(options))).Identity!;

        Assert.Equal("dir.user", identity.LoginName);
        Assert.Equal("user.one@example.org", identity.Email);
    }

    [Fact]
    public async Task A_non_string_claim_is_absent_rather_than_converted()
    {
        http.Json(TestIssuer.TokenEndpoint, StubProvider.TokenResponse(issuer.Token(claims => claims["upn"] = 12345)));

        Assert.Null((await Complete()).Identity!.Upn);
    }

    [Fact]
    public async Task Pinned_mode_validates_without_fetching_a_jwks()
    {
        var options = TestIssuer.Options(SigningKeyMode.Pinned);
        options.PinnedKeys = [new OidcPinnedKey { Kid = issuer.Key.KeyId!, PublicKey = issuer.PublicKeyPem() }];
        http.Json(TestIssuer.TokenEndpoint, StubProvider.TokenResponse(issuer.Token()));

        Assert.Equal(OidcSignInStatus.Succeeded, (await Complete(Provider(options))).Status);
        Assert.Equal(0, http.CallsTo(TestIssuer.JwksUri));
    }

    [Fact]
    public async Task Cancellation_propagates()
    {
        http.Json(TestIssuer.TokenEndpoint, StubProvider.TokenResponse(issuer.Token()));
        using var cancelled = new CancellationTokenSource();
        await cancelled.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Provider().CompleteAsync(new OidcCallback(Code, State), Pending, cancelled.Token));
    }

    [Fact]
    public void The_authorization_url_carries_pkce_s256_state_nonce_and_the_registered_redirect()
    {
        var request = Provider().BeginAuthorization("/requests/42");
        var query = QueryHelpers.ParseQuery(request.AuthorizationUrl.Query);

        Assert.StartsWith(TestIssuer.AuthorizationEndpoint + "?", request.AuthorizationUrl.AbsoluteUri, StringComparison.Ordinal);
        Assert.Equal("code", query["response_type"]);
        Assert.Equal(TestIssuer.ClientId, query["client_id"]);
        Assert.Equal(TestIssuer.RedirectUri, query["redirect_uri"]);
        Assert.Equal("openid profile email", query["scope"]);
        Assert.Equal(request.State, query["state"]);
        Assert.Equal(request.Nonce, query["nonce"]);
        Assert.Equal("S256", query["code_challenge_method"]);
        Assert.Equal(Pkce.ChallengeOf(request.CodeVerifier), query["code_challenge"]);
        Assert.False(query.ContainsKey("code_verifier"));
        Assert.Equal("/requests/42", request.ReturnUrl);
    }

    [Fact]
    public void Every_authorization_request_has_fresh_values()
    {
        var provider = Provider();
        var first = provider.BeginAuthorization("/");
        var second = provider.BeginAuthorization("/");

        Assert.NotEqual(first.State, second.State);
        Assert.NotEqual(first.Nonce, second.Nonce);
        Assert.NotEqual(first.CodeVerifier, second.CodeVerifier);
        Assert.NotEqual(first.State, first.Nonce);
    }

    [Theory]
    [InlineData("https://evil.example.org/")]
    [InlineData("//evil.example.org")]
    [InlineData("/\\evil.example.org")]
    [InlineData("")]
    public void A_return_url_that_is_not_root_relative_falls_back_to_root(string returnUrl) =>
        Assert.Equal("/", Provider().BeginAuthorization(returnUrl).ReturnUrl);

    [Fact]
    public void The_end_session_url_carries_the_hint_client_and_configured_post_logout_redirect()
    {
        var options = TestIssuer.Options();
        options.PostLogoutRedirectUri = "https://app.example.org/login";

        var url = Provider(options).BuildEndSessionUrl("raw-id-token", postLogoutRedirectUri: null);
        var query = QueryHelpers.ParseQuery(url.Query);

        Assert.Equal(TestIssuer.EndSessionEndpoint, url.GetLeftPart(UriPartial.Path));
        Assert.Equal("raw-id-token", query["id_token_hint"]);
        Assert.Equal(TestIssuer.ClientId, query["client_id"]);
        Assert.Equal("https://app.example.org/login", query["post_logout_redirect_uri"]);
    }

    [Fact]
    public void An_explicit_post_logout_redirect_wins_and_none_is_sent_when_none_is_known()
    {
        var provider = Provider();

        var explicitUrl = provider.BuildEndSessionUrl("hint", new Uri("https://app.example.org/bye"));
        var withoutUrl = provider.BuildEndSessionUrl("hint", postLogoutRedirectUri: null);

        Assert.Equal("https://app.example.org/bye", QueryHelpers.ParseQuery(explicitUrl.Query)["post_logout_redirect_uri"]);
        Assert.False(QueryHelpers.ParseQuery(withoutUrl.Query).ContainsKey("post_logout_redirect_uri"));
    }

    [Fact]
    public void An_end_session_endpoint_with_a_query_keeps_it()
    {
        var options = TestIssuer.Options();
        options.EndSessionEndpoint = "https://idp.example.org/logout?tenant=a";

        var query = QueryHelpers.ParseQuery(Provider(options).BuildEndSessionUrl("hint", postLogoutRedirectUri: null).Query);

        Assert.Equal("a", query["tenant"]);
        Assert.Equal("hint", query["id_token_hint"]);
    }

    [Fact]
    public void A_blank_hint_is_refused() =>
        Assert.Throws<ArgumentException>(() => Provider().BuildEndSessionUrl(" ", postLogoutRedirectUri: null));

    [Fact]
    public void A_disabled_provider_refuses_to_start_a_sign_in()
    {
        var options = TestIssuer.Options();
        options.Enabled = false;

        Assert.Throws<InvalidOperationException>(() => Provider(options).BeginAuthorization("/"));
    }

    [Fact]
    public void Sign_in_secrets_are_never_printed()
    {
        var request = Provider().BeginAuthorization("/");
        var identity = new OidcIdentity("sub-1", "a@example.org", "a", "a@example.org", "A", "raw-id-token");

        Assert.DoesNotContain(request.CodeVerifier, request.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain("raw-id-token", identity.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain(Verifier, Pending.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain(Code, new OidcCallback(Code, State).ToString(), StringComparison.Ordinal);
    }
}
