using System.Text;
using System.Text.Json;
using Codelab.Security.Canonical;
using Codelab.Security.Replay;
using Codelab.Security.ServiceAuth;
using Codelab.Security.Signing;
using Codelab.Security.Tests.TestSupport;
using Microsoft.Extensions.Time.Testing;

namespace Codelab.Security.Tests.ServiceAuth;

public sealed class ServiceAssertionValidatorTests
{
    private const string Caller = "attachment-service";
    private const string Audience = "starter-api";
    private static readonly Uri Target = new("https://api.example.test/api/internal/records?page=1");
    private static readonly byte[] Body = Encoding.UTF8.GetBytes("{\"a\":1}");
    private readonly FakeTimeProvider _clock = new(new DateTimeOffset(2026, 11, 4, 10, 0, 0, TimeSpan.Zero));
    private readonly (string PrivatePem, string PublicPem) _keys = TestKeys.NewEcKeyPair();

    [Fact]
    public async Task A_fresh_assertion_for_this_request_is_accepted_once_then_replay_is_refused()
    {
        using var validator = Validator();
        var request = Request(Issue());

        var first = await validator.ValidateAsync(request, CancellationToken.None);
        var replay = await validator.ValidateAsync(request, CancellationToken.None);

        Assert.Equal(ServiceAssertionOutcome.Valid, first.Outcome);
        Assert.Equal(Caller, first.ServiceName);
        Assert.Equal(ServiceAssertionOutcome.Replayed, replay.Outcome);
    }

    [Fact]
    public async Task The_assertion_is_bound_to_method_target_and_body()
    {
        using var validator = Validator();
        var token = Issue();

        Assert.Equal(ServiceAssertionOutcome.BindingMismatch, (await validator.ValidateAsync(Request(token) with { Method = "PUT" }, CancellationToken.None)).Outcome);
        Assert.Equal(ServiceAssertionOutcome.BindingMismatch, (await validator.ValidateAsync(Request(token) with { Target = "/api/internal/records?page=2" }, CancellationToken.None)).Outcome);
        Assert.Equal(ServiceAssertionOutcome.BindingMismatch, (await validator.ValidateAsync(Request(token) with { BodyHash = SignatureCanonicalForm.HashBody("x"u8) }, CancellationToken.None)).Outcome);
    }

    [Fact]
    public async Task An_expired_or_future_assertion_is_refused()
    {
        using var validator = Validator();
        var token = Issue();

        var future = Issue(clock: new FakeTimeProvider(_clock.GetUtcNow().AddMinutes(10)));
        Assert.Equal(ServiceAssertionOutcome.Expired, (await validator.ValidateAsync(Request(future), CancellationToken.None)).Outcome);

        _clock.Advance(TimeSpan.FromMinutes(2));
        Assert.Equal(ServiceAssertionOutcome.Expired, (await validator.ValidateAsync(Request(token), CancellationToken.None)).Outcome);
    }

    [Fact]
    public async Task Another_audience_another_key_or_another_prefix_is_refused()
    {
        using var validator = Validator();
        var other = TestKeys.NewEcKeyPair();

        Assert.Equal(ServiceAssertionOutcome.WrongAudience, (await validator.ValidateAsync(Request(Issue(audience: "other-api")), CancellationToken.None)).Outcome);
        Assert.Equal(ServiceAssertionOutcome.BadSignature, (await validator.ValidateAsync(Request(Issue(privatePem: other.PrivatePem)), CancellationToken.None)).Outcome);
        Assert.Equal(ServiceAssertionOutcome.UnknownKey, (await validator.ValidateAsync(Request(Issue(keyId: "s9")), CancellationToken.None)).Outcome);
        Assert.Equal(ServiceAssertionOutcome.Malformed, (await validator.ValidateAsync(Request(Issue(protocol: TestProtocols.Alternate)), CancellationToken.None)).Outcome);
    }

    [Theory]
    [InlineData("none")]
    [InlineData("HS256")]
    [InlineData("RS256")]
    public async Task Only_es256_is_accepted_whatever_the_header_claims(string algorithm)
    {
        using var validator = Validator();
        var parts = Issue().Split('.');
        var header = JsonSerializer.SerializeToUtf8Bytes(new ServiceAssertionHeader(algorithm, TestProtocols.Test.ServiceAssertionType, "s1"));
        var forged = string.Join('.', Base64UrlText.Encode(header), parts[1], parts[2]);

        Assert.Equal(ServiceAssertionOutcome.Malformed, (await validator.ValidateAsync(Request(forged), CancellationToken.None)).Outcome);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("a.b")]
    [InlineData("a.b.c")]
    public async Task A_missing_or_malformed_token_never_throws(string? token)
    {
        using var validator = Validator();

        var outcome = (await validator.ValidateAsync(Request(token!), CancellationToken.None)).Outcome;

        Assert.True(outcome is ServiceAssertionOutcome.Missing or ServiceAssertionOutcome.Malformed);
    }

    private ServiceAssertionValidator Validator()
    {
        var options = new ServiceAssertionServerOptions { Audience = Audience, TrustedServices = [new TrustedServiceOptions { ServiceName = Caller, KeyId = "s1", PublicKeyPem = _keys.PublicPem }] };
        return new ServiceAssertionValidator(options, new MemoryReplayGuard(_clock, entryLimit: 100), _clock, TestProtocols.Test);
    }

    private string Issue(string audience = Audience, string? privatePem = null, string keyId = "s1", SecurityProtocol? protocol = null, TimeProvider? clock = null)
    {
        var options = new ServiceAssertionClientOptions { ServiceName = Caller, Audience = audience, KeyId = keyId, PrivateKeyPem = privatePem ?? _keys.PrivatePem };
        using var issuer = new ServiceAssertionIssuer(options, clock ?? _clock, protocol ?? TestProtocols.Test);
        return issuer.Issue(HttpMethod.Post, Target, Body);
    }

    private static ServiceAssertionRequest Request(string token) =>
        new(token, "POST", ServiceAssertionToken.Target(Target.AbsolutePath, Target.Query), SignatureCanonicalForm.HashBody(Body));
}
