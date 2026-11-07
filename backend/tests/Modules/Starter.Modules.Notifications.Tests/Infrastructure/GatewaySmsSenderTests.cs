using System.Net;
using System.Text;
using System.Text.Json;
using Codelab.Errors;
using Codelab.Integration.Classification;
using Codelab.Integration.Gateway;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Starter.Domain.Localization;
using Starter.Modules.Notifications.Application;
using Starter.Modules.Notifications.Infrastructure.Sending;

namespace Starter.Modules.Notifications.Tests.Infrastructure;

/// <summary>A fake gateway behind the relay client: records each request and answers from a script.</summary>
internal sealed class FakeGateway(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
{
    public List<(HttpMethod Method, string Path, string Body)> Received { get; } = [];

    public static FakeGateway Answer(HttpStatusCode status, string json, string mediaType = "application/json") =>
        new(_ => new HttpResponseMessage(status) { Content = new StringContent(json, Encoding.UTF8, mediaType) });

    public static FakeGateway Problem(HttpStatusCode status, string code) =>
        Answer(status, $"{{\"status\":{(int)status},\"code\":\"{code}\",\"referenceCode\":\"GW-REF-1\"}}", "application/problem+json");

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var body = request.Content is null ? string.Empty : await request.Content.ReadAsStringAsync(cancellationToken);
        Received.Add((request.Method, request.RequestUri!.AbsolutePath, body));
        return respond(request);
    }
}

public sealed class GatewaySmsSenderTests
{
    private const string Mobile = "+10000000001";
    private static readonly Uri GatewayBase = new("https://gateway.example.test/");

    [Fact]
    public async Task An_accepted_sms_is_posted_to_the_sms_relay_with_the_gateway_language()
    {
        var gateway = FakeGateway.Answer(HttpStatusCode.OK, "{\"accepted\":true,\"providerMessageId\":\"pm-1\",\"referenceCode\":\"GW-1\"}");

        await Sender(gateway).SendAsync(new SmsMessage(Mobile, "رسالة نصية تجريبية", AppLanguage.Arabic), CancellationToken.None);

        var request = Assert.Single(gateway.Received);
        Assert.Equal(HttpMethod.Post, request.Method);
        Assert.Equal(GatewayRoutes.Sms, request.Path);
        var body = JsonSerializer.Deserialize<SmsRelayRequest>(request.Body, GatewayJson.Options)!;
        Assert.Equal((Mobile, "رسالة نصية تجريبية", GatewayLanguages.Arabic), (body.To, body.Text, body.Language));
    }

    [Theory]
    [InlineData(AppLanguage.Arabic, GatewayLanguages.Arabic)]
    [InlineData(AppLanguage.English, GatewayLanguages.English)]
    public void The_relay_language_is_the_gateway_contract_value(AppLanguage language, int expected) =>
        Assert.Equal(expected, SmsRelayFailures.RequestOf(new SmsMessage(Mobile, "x", language)).Language);

    [Theory]
    [InlineData(HttpStatusCode.ServiceUnavailable, GatewayFailureCodes.UpstreamUnavailable, true, SmsDeliveryErrorCodes.RelayUnavailable)]
    [InlineData(HttpStatusCode.BadGateway, GatewayFailureCodes.UpstreamRejected, false, SmsDeliveryErrorCodes.RelayRejected)]
    [InlineData(HttpStatusCode.NotFound, GatewayFailureCodes.ProfileDisabled, false, SmsDeliveryErrorCodes.RelayRejected)]
    public async Task A_gateway_refusal_is_classified_by_its_code(HttpStatusCode status, string gatewayCode, bool retryable, string code)
    {
        var failure = await Assert.ThrowsAsync<SmsDeliveryException>(() => Send(FakeGateway.Problem(status, gatewayCode)));

        Assert.Equal((code, retryable, gatewayCode, "GW-REF-1"), (failure.Code, failure.IsRetryable, failure.GatewayCode, failure.GatewayReferenceCode));
        Assert.IsType<GatewayRelayException>(failure.InnerException);
        Assert.DoesNotContain(Mobile, failure.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task The_gateway_refusing_this_api_is_permanent()
    {
        var failure = await Assert.ThrowsAsync<SmsDeliveryException>(() => Send(new FakeGateway(_ => new HttpResponseMessage(HttpStatusCode.Unauthorized))));

        Assert.False(failure.IsRetryable);
        Assert.Equal(SmsDeliveryErrorCodes.RelayRejected, failure.Code);
    }

    [Fact]
    public async Task An_unreachable_gateway_is_retryable()
    {
        var gateway = new FakeGateway(_ => throw new HttpRequestException(HttpRequestError.ConnectionError, "refused"));

        var failure = await Assert.ThrowsAsync<SmsDeliveryException>(() => Send(gateway));

        Assert.True(failure.IsRetryable);
        Assert.Equal(SmsDeliveryErrorCodes.RelayUnavailable, failure.Code);
    }

    [Fact]
    public async Task A_message_the_provider_did_not_accept_is_permanent()
    {
        var gateway = FakeGateway.Answer(HttpStatusCode.OK, "{\"accepted\":false,\"referenceCode\":\"GW-2\"}");

        var failure = await Assert.ThrowsAsync<SmsDeliveryException>(() => Send(gateway));

        Assert.Equal((SmsDeliveryErrorCodes.RelayRejected, false, "GW-2"), (failure.Code, failure.IsRetryable, failure.GatewayReferenceCode));
    }

    [Fact]
    public async Task A_message_outside_the_relay_bounds_is_refused_before_anything_is_sent()
    {
        var gateway = FakeGateway.Answer(HttpStatusCode.OK, "{\"accepted\":true}");
        var tooLong = new string('x', GatewayRelayLimits.MaxSmsTextLength + 1);

        var failure = await Assert.ThrowsAsync<SmsDeliveryException>(() =>
            Sender(gateway).SendAsync(new SmsMessage(Mobile, tooLong, AppLanguage.English), CancellationToken.None));

        Assert.Equal((SmsDeliveryErrorCodes.MessageInvalid, false), (failure.Code, failure.IsRetryable));
        Assert.Empty(gateway.Received);
    }

    [Theory]
    [InlineData(GatewayFailureCodes.UpstreamUnavailable, OutboundFailureKind.ConnectionFailed, true)]
    [InlineData(GatewayFailureCodes.UpstreamUnavailable, OutboundFailureKind.Timeout, true)]
    [InlineData(GatewayFailureCodes.UpstreamUnavailable, OutboundFailureKind.Throttled, true)]
    [InlineData(GatewayFailureCodes.UpstreamUnavailable, OutboundFailureKind.ServerError, true)]
    [InlineData(GatewayFailureCodes.UpstreamUnavailable, OutboundFailureKind.Unknown, true)]
    [InlineData(GatewayFailureCodes.UpstreamUnavailable, OutboundFailureKind.CertificateRejected, false)]
    [InlineData(GatewayFailureCodes.UpstreamUnavailable, OutboundFailureKind.Unauthorized, false)]
    [InlineData(GatewayFailureCodes.UpstreamUnavailable, OutboundFailureKind.ClientError, false)]
    [InlineData(GatewayFailureCodes.UpstreamRejected, OutboundFailureKind.ServerError, false)]
    [InlineData(GatewayFailureCodes.ProfileDisabled, OutboundFailureKind.ClientError, false)]
    public void Retryability_follows_the_gateway_code_and_the_outbound_failure_kind(string code, OutboundFailureKind kind, bool retryable)
    {
        var failure = new GatewayRelayException(new GatewayRelayFailure(code, new OutboundFailure(kind, StatusCode: null, IsTransient: false), ReferenceCode: null));

        Assert.Equal(retryable, SmsRelayFailures.IsRetryable(failure));
    }

    [Fact]
    public void The_classifier_records_the_sms_code_and_is_added_once()
    {
        var options = new CodelabErrorsOptions().AddSmsDeliveryFailures().AddSmsDeliveryFailures();
        var table = new FailureClassifierTable(Options.Create(options));
        var unavailable = SmsRelayFailures.From(new GatewayRelayException(new GatewayRelayFailure(
            GatewayFailureCodes.UpstreamUnavailable, new OutboundFailure(OutboundFailureKind.ConnectionFailed, StatusCode: null, IsTransient: true), ReferenceCode: null)));

        Assert.Equal(SmsDeliveryErrorCodes.RelayUnavailable, table.Resolve(unavailable).Code);
        Assert.Equal(SmsDeliveryErrorCodes.MessageInvalid, table.Resolve(SmsRelayFailures.Invalid()).Code);
        Assert.Equal(503, SmsDeliveryFailures.Of(unavailable).Status);
        Assert.Equal(502, SmsDeliveryFailures.Of(SmsRelayFailures.NotAccepted(gatewayReferenceCode: null)).Status);
        Assert.Single(options.Classifiers, classifier => classifier.GetType().GetGenericArguments().SingleOrDefault() == typeof(SmsDeliveryException));
    }

    private static Task Send(FakeGateway gateway) =>
        Sender(gateway).SendAsync(new SmsMessage(Mobile, "x", AppLanguage.English), CancellationToken.None);

    private static GatewaySmsSender Sender(HttpMessageHandler gateway) =>
        new(new GatewayRelayClient(new HttpClient(gateway, disposeHandler: false) { BaseAddress = GatewayBase }), NullLogger<GatewaySmsSender>.Instance);
}
