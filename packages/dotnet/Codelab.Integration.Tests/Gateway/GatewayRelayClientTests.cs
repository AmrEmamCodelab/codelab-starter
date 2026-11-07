using System.Net;
using Codelab.Integration.Classification;
using Codelab.Integration.Gateway;
using Codelab.Integration.Tests.Gateway.Support;
using Codelab.Security.ServiceAuth;
using Codelab.Security.Signing;
using Microsoft.Extensions.DependencyInjection;

namespace Codelab.Integration.Tests.Gateway;

/// <summary>The relay client: routes, bodies, the per-attempt assertion, retries and failure mapping.</summary>
public sealed class GatewayRelayClientTests
{
    private const string AcceptedJson = "{\"accepted\":true,\"providerMessageId\":\"m-1\"}";
    private readonly ServiceKeyPair _keys = ServiceKeyPair.Create();

    private static SmsRelayRequest Sms => new() { To = "05XXXXXXXX", Text = "Your code is 1234", Language = GatewayLanguages.Arabic };

    [Fact]
    public async Task Every_request_carries_an_assertion_the_gateway_validator_accepts_once()
    {
        var stub = RecordingHandler.Json(HttpStatusCode.OK, AcceptedJson);
        using var provider = GatewayClientHost.Build(GatewayClientHost.Settings(_keys), stub);

        await provider.GetRequiredService<GatewayRelayClient>().RelaySmsAsync(Sms);

        var sent = Assert.Single(stub.Received);
        using var validator = GatewayClientHost.Validator(_keys);
        var request = new ServiceAssertionRequest(
            sent.Assertion,
            sent.Method.Method,
            ServiceAssertionToken.Target(sent.Uri.AbsolutePath, sent.Uri.Query),
            SignatureCanonicalForm.HashBody(sent.Body));
        var first = await validator.ValidateAsync(request, CancellationToken.None);
        var replay = await validator.ValidateAsync(request, CancellationToken.None);
        Assert.Equal(ServiceAssertionOutcome.Valid, first.Outcome);
        Assert.Equal(GatewayClientHost.CallerName, first.ServiceName);
        Assert.Equal(ServiceAssertionOutcome.Replayed, replay.Outcome);
    }

    [Fact]
    public async Task The_assertion_carries_the_gateway_audience_not_the_shared_client_audience()
    {
        var stub = RecordingHandler.Json(HttpStatusCode.OK, AcceptedJson);
        using var provider = GatewayClientHost.Build(GatewayClientHost.Settings(_keys), stub);

        await provider.GetRequiredService<GatewayRelayClient>().RelaySmsAsync(Sms);

        var sent = Assert.Single(stub.Received);
        using var validator = GatewayClientHost.Validator(_keys, audience: GatewayClientHost.OtherAudience);
        var result = await validator.ValidateAsync(
            new ServiceAssertionRequest(sent.Assertion, sent.Method.Method, ServiceAssertionToken.Target(sent.Uri.AbsolutePath, sent.Uri.Query), SignatureCanonicalForm.HashBody(sent.Body)),
            CancellationToken.None);
        Assert.Equal(ServiceAssertionOutcome.WrongAudience, result.Outcome);
    }

    [Fact]
    public async Task Sms_is_posted_as_json_below_the_gateway_path_base_and_the_result_is_read()
    {
        var stub = RecordingHandler.Json(HttpStatusCode.OK, AcceptedJson);
        using var provider = GatewayClientHost.Build(GatewayClientHost.Settings(_keys), stub);

        var result = await provider.GetRequiredService<GatewayRelayClient>().RelaySmsAsync(Sms);

        var sent = Assert.Single(stub.Received);
        Assert.Equal(HttpMethod.Post, sent.Method);
        Assert.Equal(new Uri("https://gateway.example.test/dmz/relay/sms"), sent.Uri);
        Assert.Equal(GatewayMediaTypes.Json, sent.ContentType);
        Assert.Equal("{\"to\":\"05XXXXXXXX\",\"text\":\"Your code is 1234\",\"language\":1}", sent.BodyText);
        Assert.True(result.Accepted);
        Assert.Equal("m-1", result.ProviderMessageId);
        Assert.Null(result.ReferenceCode);
    }

    [Fact]
    public async Task The_token_request_form_passes_through_and_the_provider_json_comes_back_verbatim()
    {
        const string providerJson = "{\"access_token\":\"a\",\"id_token\":\"b\",\"token_type\":\"Bearer\",\"expires_in\":300}";
        var stub = RecordingHandler.Json(HttpStatusCode.OK, providerJson);
        using var provider = GatewayClientHost.Build(GatewayClientHost.Settings(_keys), stub);
        var form = new SsoTokenRelayRequest([new("grant_type", "authorization_code"), new("code", "c 1"), new("code_verifier", "v")]);

        using var document = await provider.GetRequiredService<GatewayRelayClient>().RelaySsoTokenAsync(form.ToContent());

        var sent = Assert.Single(stub.Received);
        Assert.Equal(new Uri("https://gateway.example.test/dmz/relay/sso/token"), sent.Uri);
        Assert.Equal(GatewayMediaTypes.FormUrlEncoded, sent.ContentType);
        Assert.Equal("grant_type=authorization_code&code=c+1&code_verifier=v", sent.BodyText);
        Assert.Equal(providerJson, document.RootElement.GetRawText());
    }

    [Fact]
    public async Task Jwks_is_a_get_that_is_retried_once_with_a_fresh_assertion_per_attempt()
    {
        var stub = new RecordingHandler(attempt => attempt == 1
            ? new HttpResponseMessage(HttpStatusCode.ServiceUnavailable)
            : new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("{\"keys\":[]}") });
        using var provider = GatewayClientHost.Build(GatewayClientHost.Settings(_keys), stub);

        using var jwks = await provider.GetRequiredService<GatewayRelayClient>().GetSsoJwksAsync();

        Assert.Equal(2, stub.Received.Count);
        Assert.All(stub.Received, sent => Assert.Equal(new Uri("https://gateway.example.test/dmz/relay/sso/jwks"), sent.Uri));
        Assert.Equal(2, stub.Received.Select(sent => sent.Assertion).Distinct().Count());
        Assert.Equal(0, jwks.RootElement.GetProperty("keys").GetArrayLength());
    }

    [Fact]
    public async Task A_relay_post_is_never_retried_and_a_503_maps_to_upstream_unavailable()
    {
        var stub = new RecordingHandler(_ => new HttpResponseMessage(HttpStatusCode.ServiceUnavailable));
        using var provider = GatewayClientHost.Build(GatewayClientHost.Settings(_keys), stub);

        var failure = await Assert.ThrowsAsync<GatewayRelayException>(() => provider.GetRequiredService<GatewayRelayClient>().RelaySmsAsync(Sms));

        Assert.Single(stub.Received);
        Assert.Equal(GatewayFailureCodes.UpstreamUnavailable, failure.Code);
        Assert.Equal(OutboundFailureKind.ServerError, failure.Failure.Kind);
        Assert.Equal(HttpStatusCode.ServiceUnavailable, failure.Failure.StatusCode);
    }

    [Fact]
    public async Task The_gateway_problem_code_and_reference_code_reach_the_caller()
    {
        var stub = RecordingHandler.Json(
            HttpStatusCode.NotFound,
            "{\"title\":\"x\",\"status\":404,\"code\":\"GATEWAY_PROFILE_DISABLED\",\"referenceCode\":\"GW-7Q2\"}",
            GatewayMediaTypes.ProblemJson);
        using var provider = GatewayClientHost.Build(GatewayClientHost.Settings(_keys), stub);

        var failure = await Assert.ThrowsAsync<GatewayRelayException>(() => provider.GetRequiredService<GatewayRelayClient>().RelaySmsAsync(Sms));

        Assert.Equal(GatewayFailureCodes.ProfileDisabled, failure.Code);
        Assert.Equal("GW-7Q2", failure.ReferenceCode);
        Assert.Equal(OutboundFailureKind.ClientError, failure.Failure.Kind);
    }

    [Theory]
    [InlineData("not json")]
    [InlineData("[1,2]")]
    public async Task An_unusable_success_body_maps_to_upstream_rejected(string body)
    {
        var stub = RecordingHandler.Json(HttpStatusCode.OK, body);
        using var provider = GatewayClientHost.Build(GatewayClientHost.Settings(_keys), stub);

        var failure = await Assert.ThrowsAsync<GatewayRelayException>(() => provider.GetRequiredService<GatewayRelayClient>().RelaySmsAsync(Sms));

        Assert.Equal(GatewayFailureCodes.UpstreamRejected, failure.Code);
    }

    [Fact]
    public async Task A_response_larger_than_the_limit_is_refused()
    {
        var huge = "{\"keys\":\"" + new string('a', GatewayRelayLimits.MaxResponseBytes) + "\"}";
        var stub = RecordingHandler.Json(HttpStatusCode.OK, huge);
        using var provider = GatewayClientHost.Build(GatewayClientHost.Settings(_keys), stub);

        var failure = await Assert.ThrowsAsync<GatewayRelayException>(() => provider.GetRequiredService<GatewayRelayClient>().GetSsoJwksAsync());

        Assert.Equal(GatewayFailureCodes.UpstreamRejected, failure.Code);
    }

    [Fact]
    public async Task A_connection_failure_maps_to_upstream_unavailable()
    {
        var stub = new RecordingHandler(_ => throw new HttpRequestException(HttpRequestError.ConnectionError, "refused"));
        using var provider = GatewayClientHost.Build(GatewayClientHost.Settings(_keys), stub);

        var failure = await Assert.ThrowsAsync<GatewayRelayException>(() => provider.GetRequiredService<GatewayRelayClient>().RelaySmsAsync(Sms));

        Assert.Equal(GatewayFailureCodes.UpstreamUnavailable, failure.Code);
        Assert.Equal(OutboundFailureKind.ConnectionFailed, failure.Failure.Kind);
        Assert.IsType<HttpRequestException>(failure.InnerException);
    }

    [Fact]
    public async Task A_caller_cancellation_surfaces_as_cancellation()
    {
        var stub = RecordingHandler.Json(HttpStatusCode.OK, AcceptedJson);
        using var provider = GatewayClientHost.Build(GatewayClientHost.Settings(_keys), stub);
        using var cancelled = new CancellationTokenSource();
        await cancelled.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => provider.GetRequiredService<GatewayRelayClient>().RelaySmsAsync(Sms, cancelled.Token));
    }

    [Fact]
    public async Task An_invalid_sms_is_refused_before_any_call()
    {
        var stub = RecordingHandler.Json(HttpStatusCode.OK, AcceptedJson);
        using var provider = GatewayClientHost.Build(GatewayClientHost.Settings(_keys), stub);

        await Assert.ThrowsAsync<ArgumentException>(() => provider.GetRequiredService<GatewayRelayClient>().RelaySmsAsync(new SmsRelayRequest { To = "+9665", Text = "x", Language = 3 }));

        Assert.Empty(stub.Received);
    }
}
