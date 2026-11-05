using System.Diagnostics.CodeAnalysis;
using System.Security.Claims;
using Codelab.Security.Sessions;
using Codelab.Security.Signing;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using Starter.Modules.Identity.Application;
using Starter.Modules.Identity.Infrastructure.Options;
using Starter.Modules.Identity.Infrastructure.Security;
using Starter.Modules.Identity.Tests.Fakes;
using OptionsFactory = Microsoft.Extensions.Options.Options;

namespace Starter.Modules.Identity.Tests.Infrastructure;

public sealed class AccessTokenTests
{
    private const string ProblemJsonMediaType = "application/problem+json";

    private const string SessionId = "5f0e2c9a7b3d4e1f8a6c0b2d9e7f1a3c";
    private static readonly JwtOptions Jwt = new()
    {
        Issuer = "starter-api",
        Audience = "starter-api",
        SigningKey = Convert.ToBase64String(Enumerable.Range(0, JwtOptions.MinimumSigningKeyBytes).Select(value => (byte)value).ToArray()),
    };

    [Fact]
    public async Task The_token_carries_subject_session_and_version_with_the_clock_times_and_no_permissions()
    {
        var clock = new FakeTimeProvider(TestClock.Start);
        var subject = Guid.NewGuid();
        var claims = new AccessTokenClaims(subject, SessionId, SessionVersion: 3, IssuedAt: clock.GetUtcNow(), ExpiresAt: clock.GetUtcNow().AddMinutes(15));

        var token = Issuer(SecurityEnforcementMode.Off).Issue(claims);
        var result = await ValidateAsync(token, validateLifetime: false);

        Assert.True(result.IsValid, result.Exception?.Message);
        var jwt = (JsonWebToken)result.SecurityToken;
        Assert.Equal(subject.ToString(), jwt.GetClaim(SecurityClaimTypes.Subject).Value);
        Assert.Equal(SessionId, jwt.GetClaim(SecurityClaimTypes.SessionId).Value);
        Assert.Equal("3", jwt.GetClaim(SecurityClaimTypes.SessionVersion).Value);
        Assert.Equal(TestClock.Start.UtcDateTime, jwt.IssuedAt);
        Assert.Equal(TestClock.Start.AddMinutes(15).UtcDateTime, jwt.ValidTo);
        Assert.False(jwt.TryGetClaim(SecurityClaimTypes.SessionKeyId, out _));
        Assert.DoesNotContain(jwt.Claims, claim => claim.Type.Contains("permission", StringComparison.OrdinalIgnoreCase) || claim.Type == ClaimTypes.Role);
    }

    [Fact]
    public void While_request_signing_is_on_the_token_names_the_active_session_key()
    {
        var token = Issuer(SecurityEnforcementMode.Enforce).Issue(new AccessTokenClaims(Guid.NewGuid(), SessionId, 0, TestClock.Start, TestClock.Start.AddMinutes(1)));

        Assert.Equal(FakeKeyring.KeyId, new JsonWebToken(token).GetClaim(SecurityClaimTypes.SessionKeyId).Value);
    }

    [Fact]
    public async Task An_expired_token_or_another_key_is_refused()
    {
        var past = TimeProvider.System.GetUtcNow().AddHours(-2);
        var expired = Issuer(SecurityEnforcementMode.Off).Issue(new AccessTokenClaims(Guid.NewGuid(), SessionId, 0, past, past.AddMinutes(15)));
        var otherKey = new JwtAccessTokenIssuer(
            OptionsFactory.Create(new JwtOptions { Issuer = Jwt.Issuer, Audience = Jwt.Audience, SigningKey = Convert.ToBase64String(new byte[JwtOptions.MinimumSigningKeyBytes]) }),
            KeySource(SecurityEnforcementMode.Off)).Issue(new AccessTokenClaims(Guid.NewGuid(), SessionId, 0, TestClock.Start, TestClock.Start.AddYears(10)));

        Assert.IsType<SecurityTokenExpiredException>((await ValidateAsync(expired, validateLifetime: true)).Exception);
        Assert.False((await ValidateAsync(otherKey, validateLifetime: false)).IsValid);
    }

    [Fact]
    public async Task The_bearer_check_fails_a_revoked_session_and_a_token_without_one()
    {
        var store = new MemorySessionRevocationStore(TimeProvider.System);
        await store.RevokeAsync(new SessionRevocation(SessionId, TimeProvider.System.GetUtcNow().AddHours(1)), CancellationToken.None);

        var revoked = await RunCheckAsync(store, sessionId: SessionId);
        var missing = await RunCheckAsync(store, sessionId: null);
        var open = await RunCheckAsync(store, sessionId: "fedcba9876543210fedcba9876543210");

        Assert.Equal(BearerAuthentication.RevokedSessionMessage, revoked.Result?.Failure?.Message);
        Assert.Equal(BearerAuthentication.RevokedSessionMessage, missing.Result?.Failure?.Message);
        Assert.Null(open.Result);
    }

    [Fact]
    public void The_bearer_scheme_keeps_claim_names_and_uses_the_clock()
    {
        var clock = new FakeTimeProvider(TestClock.Start);
        var options = new JwtBearerOptions();

        new BearerAuthentication(OptionsFactory.Create(Jwt), clock).Configure(JwtBearerDefaults.AuthenticationScheme, options);

        Assert.False(options.MapInboundClaims);
        Assert.Same(clock, options.TimeProvider);
        Assert.Equal(Jwt.Issuer, options.TokenValidationParameters.ValidIssuer);
        Assert.Equal([SigningKey.Algorithm], options.TokenValidationParameters.ValidAlgorithms);
        Assert.Equal(SecurityClaimTypes.Subject, options.TokenValidationParameters.NameClaimType);
    }

    [Fact]
    public async Task TheChallengeAnswers401ProblemJsonWithTheBearerHeader()
    {
        var http = new DefaultHttpContext { RequestServices = new ServiceCollection().BuildServiceProvider() };
        http.Response.Body = new MemoryStream();
        var context = new JwtBearerChallengeContext(
            http,
            new AuthenticationScheme(JwtBearerDefaults.AuthenticationScheme, displayName: null, typeof(JwtBearerHandler)),
            new JwtBearerOptions(),
            new AuthenticationProperties());

        await BearerAuthentication.WriteChallengeProblemAsync(context);

        Assert.True(context.Handled);
        Assert.Equal(StatusCodes.Status401Unauthorized, http.Response.StatusCode);
        Assert.Equal(JwtBearerDefaults.AuthenticationScheme, http.Response.Headers.WWWAuthenticate.ToString());
        Assert.StartsWith(ProblemJsonMediaType, http.Response.ContentType, StringComparison.Ordinal);
        Assert.True(http.Response.Body.Length > 0);
    }

    private static async Task<TokenValidatedContext> RunCheckAsync(ISessionRevocationStore store, string? sessionId)
    {
        var http = new DefaultHttpContext { RequestServices = new ServiceCollection().AddSingleton(store).BuildServiceProvider() };
        var claims = sessionId is null ? [] : new[] { new Claim(SecurityClaimTypes.SessionId, sessionId) };
        var context = new TokenValidatedContext(
            http,
            new AuthenticationScheme(JwtBearerDefaults.AuthenticationScheme, displayName: null, typeof(JwtBearerHandler)),
            new JwtBearerOptions())
        {
            Principal = new ClaimsPrincipal(new ClaimsIdentity(claims, JwtBearerDefaults.AuthenticationScheme)),
        };
        await BearerAuthentication.RejectRevokedSessionAsync(context);
        return context;
    }

    private static Task<TokenValidationResult> ValidateAsync(string token, bool validateLifetime)
    {
        var options = new JwtBearerOptions();
        new BearerAuthentication(OptionsFactory.Create(Jwt), TimeProvider.System).Configure(options);
        var parameters = options.TokenValidationParameters.Clone();
        parameters.ValidateLifetime = validateLifetime;
        return new JsonWebTokenHandler().ValidateTokenAsync(token, parameters);
    }

    private static JwtAccessTokenIssuer Issuer(SecurityEnforcementMode mode) => new(OptionsFactory.Create(Jwt), KeySource(mode));

    private static SessionKeyIdSource KeySource(SecurityEnforcementMode mode)
    {
        var services = new ServiceCollection()
            .AddSingleton<ISigningKeyring, FakeKeyring>()
            .Configure<RequestSigningOptions>(options => options.Mode = mode)
            .BuildServiceProvider();
        return ActivatorUtilities.CreateInstance<SessionKeyIdSource>(services);
    }

    private sealed class FakeKeyring : ISigningKeyring
    {
        public const string KeyId = "k1";

        public string ActiveKeyId => KeyId;

        public bool TryGet(string? keyId, [NotNullWhen(true)] out byte[]? masterKey)
        {
            masterKey = null;
            return false;
        }
    }
}
