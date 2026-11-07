using System.Net;
using System.Text;
using Codelab.Gateway.Api.Relay;
using Codelab.Gateway.Api.Sso;
using Codelab.Integration.Gateway;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Hosting.Internal;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;

namespace Codelab.Gateway.Api.Tests.Unit;

public sealed class TokenRequestGuardTests
{
    [Theory]
    [InlineData("grant_type=authorization_code&code=abc&code_verifier=xyz")]
    [InlineData("grant_type=refresh_token&refresh_token=r%2B1")]
    [InlineData("flag")]
    public void A_well_formed_form_passes(string form) => Assert.Null(TokenRequestGuard.Problem(Encoding.ASCII.GetBytes(form)));

    [Theory]
    [InlineData("")]
    [InlineData("=value")]
    [InlineData("grant_type=x&&code=y")]
    public void A_form_with_an_empty_or_nameless_field_is_refused(string form) => Assert.NotNull(TokenRequestGuard.Problem(Encoding.ASCII.GetBytes(form)));

    [Fact]
    public void A_form_with_raw_non_ascii_bytes_is_refused() =>
        Assert.NotNull(TokenRequestGuard.Problem(Encoding.UTF8.GetBytes("code=رمز")));

    [Fact]
    public void A_form_with_control_characters_is_refused() =>
        Assert.NotNull(TokenRequestGuard.Problem(Encoding.ASCII.GetBytes("code=a\r\nb")));

    [Fact]
    public void A_form_with_too_many_fields_is_refused()
    {
        var form = string.Join('&', Enumerable.Range(0, GatewayRelayLimits.MaxSsoTokenFormFields + 1).Select(index => $"f{index}=v"));

        Assert.NotNull(TokenRequestGuard.Problem(Encoding.ASCII.GetBytes(form)));
    }

    [Fact]
    public void A_form_with_the_most_fields_passes()
    {
        var form = string.Join('&', Enumerable.Range(0, GatewayRelayLimits.MaxSsoTokenFormFields).Select(index => $"f{index}=v"));

        Assert.Null(TokenRequestGuard.Problem(Encoding.ASCII.GetBytes(form)));
    }
}

public sealed class SsoDocumentsTests
{
    [Theory]
    [InlineData("""{"access_token":"a"}""", true)]
    [InlineData("""{"error":"invalid_grant"}""", true)]
    [InlineData("[]", false)]
    [InlineData("<html></html>", false)]
    [InlineData("", false)]
    public void A_token_answer_must_be_a_json_object(string body, bool expected) =>
        Assert.Equal(expected, SsoDocuments.IsJsonObject(Encoding.UTF8.GetBytes(body)));

    [Theory]
    [InlineData("""{"keys":[]}""", true)]
    [InlineData("""{"keys":[{"kty":"RSA"}]}""", true)]
    [InlineData("""{"keys":{}}""", false)]
    [InlineData("""{"issuer":"x"}""", false)]
    [InlineData("not json", false)]
    public void A_jwks_document_needs_a_keys_array(string body, bool expected) =>
        Assert.Equal(expected, SsoDocuments.IsJwks(Encoding.UTF8.GetBytes(body)));
}

public sealed class SsoRelayOptionsValidatorTests
{
    [Fact]
    public void Https_endpoints_and_a_bounded_cache_are_valid() =>
        Assert.True(Validate(Options(), isDevelopment: false).Succeeded);

    [Fact]
    public void Http_endpoints_are_valid_only_in_development()
    {
        var options = Options(token: "http://localhost:5190/token", jwks: "http://localhost:5190/jwks");

        Assert.True(Validate(options, isDevelopment: true).Succeeded);
        Assert.True(Validate(options, isDevelopment: false).Failed);
    }

    [Fact]
    public void Missing_endpoints_name_both_settings()
    {
        var result = Validate(new SsoRelayOptions(), isDevelopment: false);

        Assert.Contains("Gateway:Sso:TokenEndpoint", result.FailureMessage, StringComparison.Ordinal);
        Assert.Contains("Gateway:Sso:JwksUri", result.FailureMessage, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(25 * 60)]
    public void A_cache_duration_outside_zero_to_a_day_is_refused(int minutes)
    {
        var options = Options();
        options.JwksCacheDuration = TimeSpan.FromMinutes(minutes);

        Assert.Contains("JwksCacheDuration", Validate(options, isDevelopment: false).FailureMessage, StringComparison.Ordinal);
    }

    internal static SsoRelayOptions Options(string token = "https://idp.example.test/token", string jwks = "https://idp.example.test/jwks") =>
        new() { TokenEndpoint = new Uri(token), JwksUri = new Uri(jwks) };

    private static ValidateOptionsResult Validate(SsoRelayOptions options, bool isDevelopment) =>
        new SsoRelayOptionsValidator(Environment(isDevelopment)).Validate(name: null, options);

    internal static IHostEnvironment Environment(bool isDevelopment) =>
        new HostingEnvironment { EnvironmentName = isDevelopment ? Environments.Development : Environments.Production };
}

public sealed class JwksCacheTests : IDisposable
{
    private static readonly byte[] Jwks = """{"keys":[]}"""u8.ToArray();

    private readonly FakeTimeProvider _clock = new(new DateTimeOffset(2026, 11, 7, 8, 0, 0, TimeSpan.Zero));
    private readonly FakeSsoUpstream _upstream = new();
    private readonly JwksCacheStore _store = new();

    [Fact]
    public async Task A_fresh_document_is_served_from_memory()
    {
        var cache = Cache(TimeSpan.FromMinutes(10));

        await cache.GetAsync(CancellationToken.None);
        _clock.Advance(TimeSpan.FromMinutes(9));
        var second = await cache.GetAsync(CancellationToken.None);

        Assert.True(second.Succeeded);
        Assert.Equal(1, _upstream.JwksCalls);
    }

    [Fact]
    public async Task An_expired_document_is_fetched_again()
    {
        var cache = Cache(TimeSpan.FromMinutes(10));

        await cache.GetAsync(CancellationToken.None);
        _clock.Advance(TimeSpan.FromMinutes(10));
        await cache.GetAsync(CancellationToken.None);

        Assert.Equal(2, _upstream.JwksCalls);
    }

    [Fact]
    public async Task A_zero_duration_never_caches()
    {
        var cache = Cache(TimeSpan.Zero);

        await cache.GetAsync(CancellationToken.None);
        await cache.GetAsync(CancellationToken.None);

        Assert.Equal(2, _upstream.JwksCalls);
    }

    [Fact]
    public async Task A_failed_fetch_is_answered_and_not_cached()
    {
        var cache = Cache(TimeSpan.FromMinutes(10));
        _upstream.Next = UpstreamResult.Failed(UpstreamFailureMapper.Unusable(HttpStatusCode.ServiceUnavailable));

        var first = await cache.GetAsync(CancellationToken.None);
        _upstream.Next = UpstreamResult.Success(HttpStatusCode.OK, Jwks);
        var second = await cache.GetAsync(CancellationToken.None);

        Assert.False(first.Succeeded);
        Assert.True(second.Succeeded);
        Assert.Equal(2, _upstream.JwksCalls);
    }

    [Fact]
    public async Task A_document_without_keys_is_rejected_and_not_cached()
    {
        var cache = Cache(TimeSpan.FromMinutes(10));
        _upstream.Next = UpstreamResult.Success(HttpStatusCode.OK, """{"x":1}"""u8.ToArray());

        var result = await cache.GetAsync(CancellationToken.None);

        Assert.Equal(GatewayFailureCodes.UpstreamRejected, result.Failure!.Code);
        Assert.Null(_store.FreshAt(_clock.GetUtcNow()));
    }

    [Fact]
    public async Task Concurrent_misses_fetch_once()
    {
        var cache = Cache(TimeSpan.FromMinutes(10));

        await Task.WhenAll(Enumerable.Range(0, 8).Select(_ => cache.GetAsync(CancellationToken.None)));

        Assert.Equal(1, _upstream.JwksCalls);
    }

    public void Dispose() => _store.Dispose();

    private JwksCache Cache(TimeSpan duration)
    {
        var options = SsoRelayOptionsValidatorTests.Options();
        options.JwksCacheDuration = duration;
        return new JwksCache(_upstream, _store, Microsoft.Extensions.Options.Options.Create(options), _clock, NullLogger<JwksCache>.Instance);
    }

    private sealed class FakeSsoUpstream : ISsoUpstream
    {
        private int _jwksCalls;

        public UpstreamResult Next { get; set; } = UpstreamResult.Success(HttpStatusCode.OK, Jwks);

        public int JwksCalls => Volatile.Read(ref _jwksCalls);

        public Task<UpstreamResult> ExchangeTokenAsync(byte[] form, CancellationToken cancellationToken) => throw new NotSupportedException();

        public async Task<UpstreamResult> GetJwksAsync(CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref _jwksCalls);
            await Task.Yield();
            return Next;
        }
    }
}
