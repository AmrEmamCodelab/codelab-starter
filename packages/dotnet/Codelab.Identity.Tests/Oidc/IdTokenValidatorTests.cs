using System.Security.Cryptography;
using Codelab.Identity.Oidc;
using Codelab.Identity.Tests.TestSupport;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using Microsoft.IdentityModel.Tokens;

namespace Codelab.Identity.Tests.Oidc;

public sealed class IdTokenValidatorTests : IDisposable
{
    private readonly FakeTimeProvider clock = new(new DateTimeOffset(2026, 11, 5, 9, 0, 0, TimeSpan.Zero));
    private readonly TestIssuer issuer;

    public IdTokenValidatorTests() => issuer = new TestIssuer(clock);

    public void Dispose() => issuer.Dispose();

    private OidcIdTokenValidator Validator(OidcOptions? options = null, IIdTokenSigningKeySource? keys = null) =>
        new(Options.Create(options ?? TestIssuer.Options()), keys ?? new FixedKeys([issuer.Key]), clock, NullLogger<OidcIdTokenValidator>.Instance);

    private Task<IdTokenValidationResult> Validate(string token, OidcOptions? options = null, IIdTokenSigningKeySource? keys = null) =>
        Validator(options, keys).ValidateAsync(token, TestIssuer.Nonce, CancellationToken.None);

    [Fact]
    public async Task A_correctly_signed_token_for_this_sign_in_is_valid()
    {
        var result = await Validate(issuer.Token());

        Assert.Equal(IdTokenValidationStatus.Valid, result.Status);
        Assert.Equal(TestIssuer.Subject, result.Token!.Subject);
    }

    [Fact]
    public async Task An_unsigned_alg_none_token_is_refused_whatever_the_keys() =>
        Assert.Equal(IdTokenValidationStatus.Invalid, (await Validate(issuer.Unsigned())).Status);

    [Fact]
    public async Task An_hs256_token_keyed_with_the_rsa_public_key_is_refused()
    {
        // Key confusion: the attacker HMACs with the public key bytes, hoping the verifier treats them as a secret.
        var publicBytes = issuer.Rsa.ExportSubjectPublicKeyInfo();
        var forged = TestIssuer.Sign(issuer.Claims(), new SigningCredentials(new SymmetricSecurityKey(publicBytes), SecurityAlgorithms.HmacSha256));
        var keys = new FixedKeys([issuer.Key, new SymmetricSecurityKey(publicBytes)]);

        Assert.Equal(IdTokenValidationStatus.Invalid, (await Validate(forged, keys: keys)).Status);
    }

    [Fact]
    public async Task An_algorithm_off_the_allowlist_is_refused_before_the_keys_are_asked()
    {
        var options = TestIssuer.Options();
        options.AllowedAlgorithms = [SecurityAlgorithms.RsaSha512];
        var keys = new FixedKeys([issuer.Key]);

        Assert.Equal(IdTokenValidationStatus.Invalid, (await Validate(issuer.Token(), options, keys)).Status);
        Assert.Equal(0, keys.Calls);
    }

    [Fact]
    public async Task Hs256_in_the_allowlist_is_still_never_accepted()
    {
        var options = TestIssuer.Options();
        options.AllowedAlgorithms = [SecurityAlgorithms.HmacSha256];
        var secret = new SymmetricSecurityKey(RandomNumberGenerator.GetBytes(32));
        var token = TestIssuer.Sign(issuer.Claims(), new SigningCredentials(secret, SecurityAlgorithms.HmacSha256));

        Assert.Equal(IdTokenValidationStatus.Invalid, (await Validate(token, options, new FixedKeys([secret]))).Status);
    }

    [Fact]
    public async Task A_token_signed_by_another_key_is_refused()
    {
        using var other = new TestIssuer(clock, kid: issuer.Key.KeyId!);

        Assert.Equal(IdTokenValidationStatus.Invalid, (await Validate(other.Token())).Status);
    }

    [Theory]
    [InlineData("nonce", "another-sign-in")]
    [InlineData("iss", "https://evil.example.org")]
    [InlineData("iss", "https://idp.example.org/")]
    [InlineData("aud", "another-client")]
    [InlineData("sub", "")]
    [InlineData("azp", "another-client")]
    public async Task A_wrong_claim_is_refused(string claim, string value) =>
        Assert.Equal(IdTokenValidationStatus.Invalid, (await Validate(issuer.Token(claims => claims[claim] = value))).Status);

    [Theory]
    [InlineData("nonce")]
    [InlineData("sub")]
    [InlineData("exp")]
    public async Task A_missing_required_claim_is_refused(string claim) =>
        Assert.Equal(IdTokenValidationStatus.Invalid, (await Validate(issuer.Token(claims => claims[claim] = null))).Status);

    [Fact]
    public async Task An_expired_token_is_refused_beyond_the_clock_skew()
    {
        var token = issuer.Token();
        clock.Advance(TimeSpan.FromMinutes(5) + TimeSpan.FromSeconds(OidcOptions.DefaultClockSkewSeconds) + TimeSpan.FromSeconds(1));

        Assert.Equal(IdTokenValidationStatus.Invalid, (await Validate(token)).Status);
    }

    [Fact]
    public async Task A_just_expired_token_is_accepted_within_the_clock_skew()
    {
        var token = issuer.Token();
        clock.Advance(TimeSpan.FromMinutes(6));

        Assert.Equal(IdTokenValidationStatus.Valid, (await Validate(token)).Status);
    }

    [Fact]
    public async Task A_token_not_yet_valid_beyond_the_skew_is_refused()
    {
        var future = clock.GetUtcNow().AddMinutes(10).ToUnixTimeSeconds();
        var token = issuer.Token(claims => { claims["nbf"] = future; claims["exp"] = future + 300; });

        Assert.Equal(IdTokenValidationStatus.Invalid, (await Validate(token)).Status);
    }

    [Fact]
    public async Task Several_audiences_need_an_azp_naming_this_client()
    {
        var audiences = new[] { TestIssuer.ClientId, "another-api" };

        Assert.Equal(IdTokenValidationStatus.Invalid, (await Validate(issuer.Token(claims => claims["aud"] = audiences))).Status);
        Assert.Equal(IdTokenValidationStatus.Valid, (await Validate(issuer.Token(claims => { claims["aud"] = audiences; claims["azp"] = TestIssuer.ClientId; }))).Status);
    }

    [Theory]
    [InlineData("")]
    [InlineData("not-a-jwt")]
    [InlineData("a.b")]
    public async Task A_malformed_token_is_refused(string token) =>
        Assert.Equal(IdTokenValidationStatus.Invalid, (await Validate(token)).Status);

    [Fact]
    public async Task No_obtainable_key_set_is_keys_unavailable() =>
        Assert.Equal(IdTokenValidationStatus.KeysUnavailable, (await Validate(issuer.Token(), keys: new FixedKeys(null))).Status);

    [Fact]
    public async Task The_kid_of_the_token_is_passed_to_the_key_source()
    {
        var keys = new FixedKeys([issuer.Key]);

        await Validate(issuer.Token(), keys: keys);

        Assert.Equal(issuer.Key.KeyId, keys.LastKid);
    }

    [Fact]
    public async Task An_ec_key_and_es256_validate_when_allowed()
    {
        using var ec = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var key = new ECDsaSecurityKey(ec) { KeyId = "ec-1" };
        var options = TestIssuer.Options();
        options.AllowedAlgorithms = [SecurityAlgorithms.EcdsaSha256];
        var token = TestIssuer.Sign(issuer.Claims(), new SigningCredentials(key, SecurityAlgorithms.EcdsaSha256));

        Assert.Equal(IdTokenValidationStatus.Valid, (await Validate(token, options, new FixedKeys([key]))).Status);
    }

    private sealed class FixedKeys(IReadOnlyList<SecurityKey>? keys) : IIdTokenSigningKeySource
    {
        public int Calls { get; private set; }

        public string? LastKid { get; private set; }

        public Task<IReadOnlyList<SecurityKey>?> GetSigningKeysAsync(string? kid, CancellationToken cancellationToken)
        {
            Calls++;
            LastKid = kid;
            return Task.FromResult(keys);
        }
    }
}
