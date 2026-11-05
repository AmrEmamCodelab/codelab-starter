using System.Net;
using System.Security.Cryptography;
using Codelab.Identity.Oidc;
using Codelab.Identity.Tests.TestSupport;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;

namespace Codelab.Identity.Tests.Oidc;

public sealed class SigningKeySourceTests : IDisposable
{
    private readonly FakeTimeProvider clock = new(new DateTimeOffset(2026, 11, 5, 9, 0, 0, TimeSpan.Zero));
    private readonly TestIssuer issuer;
    private readonly StubProvider provider = new();

    public SigningKeySourceTests()
    {
        issuer = new TestIssuer(clock);
        provider.Json(TestIssuer.JwksUri, issuer.Jwks());
    }

    public void Dispose()
    {
        issuer.Dispose();
        provider.Dispose();
    }

    private JwksSigningKeySource Jwks() =>
        new(new StubClientFactory(provider), Options.Create(TestIssuer.Options()), clock, NullLogger<JwksSigningKeySource>.Instance);

    private int Fetches => provider.CallsTo(TestIssuer.JwksUri);

    [Fact]
    public async Task The_set_is_fetched_once_and_served_from_cache()
    {
        using var source = Jwks();

        var first = await source.GetSigningKeysAsync("key-1", CancellationToken.None);
        var second = await source.GetSigningKeysAsync("key-1", CancellationToken.None);

        Assert.Equal("key-1", Assert.Single(first!).KeyId);
        Assert.Same(first, second);
        Assert.Equal(1, Fetches);
    }

    [Fact]
    public async Task An_unknown_kid_forces_one_refetch_so_a_rotation_is_picked_up()
    {
        using var source = Jwks();
        await source.GetSigningKeysAsync("key-1", CancellationToken.None);
        using var rotated = RSA.Create(2048);
        provider.Json(TestIssuer.JwksUri, TestIssuer.JwksOf([TestIssuer.JwkOf(rotated, "key-2")]));

        var keys = await source.GetSigningKeysAsync("key-2", CancellationToken.None);

        Assert.Equal("key-2", Assert.Single(keys!).KeyId);
        Assert.Equal(2, Fetches);
    }

    [Fact]
    public async Task Unknown_kids_cannot_make_the_host_hammer_the_provider()
    {
        using var source = Jwks();
        await source.GetSigningKeysAsync("key-1", CancellationToken.None);

        await source.GetSigningKeysAsync("invented-1", CancellationToken.None);
        await source.GetSigningKeysAsync("invented-2", CancellationToken.None);
        await source.GetSigningKeysAsync("invented-3", CancellationToken.None);
        Assert.Equal(2, Fetches);

        clock.Advance(JwksSigningKeySource.UnknownKidRefreshInterval);
        await source.GetSigningKeysAsync("invented-4", CancellationToken.None);
        Assert.Equal(3, Fetches);
    }

    [Fact]
    public async Task A_set_older_than_its_lifetime_is_refetched()
    {
        using var source = Jwks();
        await source.GetSigningKeysAsync(kid: null, CancellationToken.None);

        clock.Advance(JwksSigningKeySource.CacheLifetime);
        await source.GetSigningKeysAsync(kid: null, CancellationToken.None);

        Assert.Equal(2, Fetches);
    }

    [Fact]
    public async Task A_failed_refresh_serves_the_stale_set_and_backs_off()
    {
        using var source = Jwks();
        var original = await source.GetSigningKeysAsync("key-1", CancellationToken.None);
        provider.Json(TestIssuer.JwksUri, "{}", HttpStatusCode.ServiceUnavailable);
        clock.Advance(JwksSigningKeySource.CacheLifetime);

        Assert.Same(original, await source.GetSigningKeysAsync("key-1", CancellationToken.None));
        Assert.Same(original, await source.GetSigningKeysAsync("key-1", CancellationToken.None));
        Assert.Equal(2, Fetches);

        clock.Advance(JwksSigningKeySource.RefreshRetryBackoff);
        await source.GetSigningKeysAsync("key-1", CancellationToken.None);
        Assert.Equal(3, Fetches);
    }

    [Fact]
    public async Task A_set_beyond_its_hard_lifetime_is_never_served()
    {
        using var source = Jwks();
        await source.GetSigningKeysAsync("key-1", CancellationToken.None);
        provider.Throws(TestIssuer.JwksUri, new HttpRequestException("down"));
        clock.Advance(JwksSigningKeySource.MaxStaleness);

        Assert.Null(await source.GetSigningKeysAsync("key-1", CancellationToken.None));
    }

    [Fact]
    public async Task No_set_and_an_unreachable_provider_is_null()
    {
        provider.Throws(TestIssuer.JwksUri, new HttpRequestException("down"));
        using var source = Jwks();

        Assert.Null(await source.GetSigningKeysAsync("key-1", CancellationToken.None));
    }

    [Theory]
    [InlineData("not json")]
    [InlineData("[1,2]")]
    public async Task A_malformed_set_is_null(string body)
    {
        provider.Json(TestIssuer.JwksUri, body);
        using var source = Jwks();

        Assert.Null(await source.GetSigningKeysAsync("key-1", CancellationToken.None));
    }

    [Fact]
    public async Task Symmetric_and_encryption_keys_are_dropped_from_the_set()
    {
        var oct = new { kty = "oct", kid = "hmac-1", k = Convert.ToBase64String(RandomNumberGenerator.GetBytes(16)) };
        using var encryption = RSA.Create(2048);
        var parameters = encryption.ExportParameters(includePrivateParameters: false);
        var enc = new { kty = "RSA", use = "enc", kid = "enc-1", n = Convert.ToBase64String(parameters.Modulus!), e = "AQAB" };
        provider.Json(TestIssuer.JwksUri, issuer.Jwks(oct, enc));
        using var source = Jwks();

        var keys = await source.GetSigningKeysAsync(kid: null, CancellationToken.None);

        Assert.Equal(["key-1"], keys!.Select(key => key.KeyId));
    }

    [Fact]
    public async Task A_set_with_too_many_keys_is_refused()
    {
        using var rsa = RSA.Create(2048);
        provider.Json(TestIssuer.JwksUri, TestIssuer.JwksOf(Enumerable.Range(0, JwksSigningKeySource.MaxKeys + 1).Select(index => TestIssuer.JwkOf(rsa, $"k{index}"))));
        using var source = Jwks();

        Assert.Null(await source.GetSigningKeysAsync(kid: null, CancellationToken.None));
    }

    [Fact]
    public async Task Cancellation_propagates()
    {
        using var source = Jwks();
        using var cancelled = new CancellationTokenSource();
        await cancelled.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => source.GetSigningKeysAsync("key-1", cancelled.Token));
    }

    [Fact]
    public async Task Pinned_pem_and_base64_der_keys_are_served_with_their_kids()
    {
        var options = TestIssuer.Options(SigningKeyMode.Pinned);
        options.PinnedKeys =
        [
            new OidcPinnedKey { Kid = "pem", PublicKey = issuer.PublicKeyPem() },
            new OidcPinnedKey { Kid = " der ", PublicKey = Convert.ToBase64String(issuer.Rsa.ExportSubjectPublicKeyInfo()) },
        ];

        var keys = await new PinnedSigningKeySource(Options.Create(options)).GetSigningKeysAsync("anything", CancellationToken.None);

        Assert.Equal(["pem", "der"], keys!.Select(key => key.KeyId));
    }

    [Fact]
    public void A_pinned_ec_key_is_accepted()
    {
        using var ec = ECDsa.Create(ECCurve.NamedCurves.nistP256);

        Assert.True(PinnedSigningKeySource.TryRead(new OidcPinnedKey { PublicKey = ec.ExportSubjectPublicKeyInfoPem() }, out var key, out _));
        Assert.Null(key.KeyId);
    }

    [Fact]
    public void A_pinned_private_key_is_refused_without_echoing_it()
    {
        var privatePem = issuer.Rsa.ExportPkcs8PrivateKeyPem();

        Assert.False(PinnedSigningKeySource.TryRead(new OidcPinnedKey { PublicKey = privatePem }, out _, out var problem));
        Assert.Contains("private key", problem, StringComparison.Ordinal);
        Assert.DoesNotContain(privatePem[40..60], problem, StringComparison.Ordinal);
    }

    [Fact]
    public void A_pinned_rsa_key_shorter_than_2048_bits_is_refused()
    {
        using var weak = RSA.Create(1024);

        Assert.False(PinnedSigningKeySource.TryRead(new OidcPinnedKey { PublicKey = weak.ExportSubjectPublicKeyInfoPem() }, out _, out var problem));
        Assert.Contains("2048", problem, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("")]
    [InlineData("not a key")]
    [InlineData("-----BEGIN PUBLIC KEY-----\nAAAA\n-----END PUBLIC KEY-----")]
    public void Garbage_is_refused(string value) =>
        Assert.False(PinnedSigningKeySource.TryRead(new OidcPinnedKey { PublicKey = value }, out _, out _));

    [Fact]
    public void An_unusable_pinned_key_fails_construction()
    {
        var options = TestIssuer.Options(SigningKeyMode.Pinned);
        options.PinnedKeys = [new OidcPinnedKey { PublicKey = "garbage" }];

        Assert.Throws<InvalidOperationException>(() => new PinnedSigningKeySource(Options.Create(options)));
    }
}
