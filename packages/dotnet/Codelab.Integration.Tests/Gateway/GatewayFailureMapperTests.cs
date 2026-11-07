using System.Net;
using System.Text;
using Codelab.Integration.Classification;
using Codelab.Integration.Gateway;
using Polly.Timeout;

namespace Codelab.Integration.Tests.Gateway;

/// <summary>Failure mapping through OutboundFailureClassifier, and reading the gateway's problem body.</summary>
public sealed class GatewayFailureMapperTests
{
    [Theory]
    [InlineData(HttpStatusCode.BadRequest, GatewayFailureCodes.UpstreamRejected, OutboundFailureKind.ClientError)]
    [InlineData(HttpStatusCode.NotFound, GatewayFailureCodes.UpstreamRejected, OutboundFailureKind.ClientError)]
    [InlineData(HttpStatusCode.Unauthorized, GatewayFailureCodes.UpstreamRejected, OutboundFailureKind.Unauthorized)]
    [InlineData(HttpStatusCode.Forbidden, GatewayFailureCodes.UpstreamRejected, OutboundFailureKind.Unauthorized)]
    [InlineData(HttpStatusCode.RequestTimeout, GatewayFailureCodes.UpstreamUnavailable, OutboundFailureKind.Timeout)]
    [InlineData(HttpStatusCode.TooManyRequests, GatewayFailureCodes.UpstreamUnavailable, OutboundFailureKind.Throttled)]
    [InlineData(HttpStatusCode.BadGateway, GatewayFailureCodes.UpstreamUnavailable, OutboundFailureKind.ServerError)]
    [InlineData(HttpStatusCode.ServiceUnavailable, GatewayFailureCodes.UpstreamUnavailable, OutboundFailureKind.ServerError)]
    [InlineData(HttpStatusCode.Redirect, GatewayFailureCodes.UpstreamUnavailable, OutboundFailureKind.Unknown)]
    public void A_status_without_a_gateway_code_maps_by_its_classification(HttpStatusCode status, string code, OutboundFailureKind kind)
    {
        var failure = GatewayFailureMapper.FromResponse(status, problem: null);

        Assert.Equal(code, failure.Code);
        Assert.Equal(kind, failure.Outbound.Kind);
        Assert.Equal(status, failure.Outbound.StatusCode);
        Assert.Null(failure.ReferenceCode);
    }

    [Fact]
    public void The_gateway_code_wins_over_the_status()
    {
        var failure = GatewayFailureMapper.FromResponse(HttpStatusCode.BadGateway, new GatewayProblemBody(GatewayFailureCodes.UpstreamRejected, "GW-1"));

        Assert.Equal(GatewayFailureCodes.UpstreamRejected, failure.Code);
        Assert.Equal("GW-1", failure.ReferenceCode);
        Assert.True(failure.Outbound.IsTransient);
    }

    [Fact]
    public void An_unknown_gateway_code_is_ignored() =>
        Assert.Equal(GatewayFailureCodes.UpstreamUnavailable, GatewayFailureMapper.FromResponse(HttpStatusCode.ServiceUnavailable, new GatewayProblemBody("SOMETHING_ELSE", null)).Code);

    [Fact]
    public void Transport_exceptions_map_to_upstream_unavailable()
    {
        var timeout = GatewayFailureMapper.FromException(new TimeoutRejectedException());
        var refused = GatewayFailureMapper.FromException(new HttpRequestException(HttpRequestError.ConnectionError, "refused"));
        var tls = GatewayFailureMapper.FromException(new HttpRequestException(HttpRequestError.SecureConnectionError, "tls"));

        Assert.Equal((GatewayFailureCodes.UpstreamUnavailable, OutboundFailureKind.Timeout), (timeout.Code, timeout.Outbound.Kind));
        Assert.Equal((GatewayFailureCodes.UpstreamUnavailable, OutboundFailureKind.ConnectionFailed), (refused.Code, refused.Outbound.Kind));
        Assert.Equal((GatewayFailureCodes.UpstreamUnavailable, OutboundFailureKind.CertificateRejected), (tls.Code, tls.Outbound.Kind));
    }

    [Fact]
    public void Only_transport_failures_are_mapped_never_a_caller_cancellation_or_a_bug()
    {
        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();

        Assert.True(GatewayFailureMapper.IsTransportFailure(new HttpRequestException("x"), CancellationToken.None));
        Assert.True(GatewayFailureMapper.IsTransportFailure(new TimeoutRejectedException(), CancellationToken.None));
        Assert.True(GatewayFailureMapper.IsTransportFailure(new IOException("reset"), CancellationToken.None));
        Assert.True(GatewayFailureMapper.IsTransportFailure(new TaskCanceledException(), CancellationToken.None));
        Assert.False(GatewayFailureMapper.IsTransportFailure(new TaskCanceledException(), cancelled.Token));
        Assert.False(GatewayFailureMapper.IsTransportFailure(new InvalidOperationException(), CancellationToken.None));
    }

    [Fact]
    public void An_unusable_body_is_upstream_rejected() =>
        Assert.Equal(GatewayFailureCodes.UpstreamRejected, GatewayFailureMapper.Unusable(HttpStatusCode.OK).Code);

    [Theory]
    [InlineData("{\"code\":\"GATEWAY_PROFILE_DISABLED\",\"referenceCode\":\"R1\"}", "GATEWAY_PROFILE_DISABLED", "R1")]
    [InlineData("{\"code\":7}", null, null)]
    [InlineData("{}", null, null)]
    public void A_problem_object_is_read(string json, string? code, string? referenceCode)
    {
        var problem = GatewayProblemBody.TryRead(Encoding.UTF8.GetBytes(json));

        Assert.Equal(new GatewayProblemBody(code, referenceCode), problem);
    }

    [Theory]
    [InlineData("")]
    [InlineData("<html/>")]
    [InlineData("[1]")]
    [InlineData("\"text\"")]
    public void Anything_else_is_no_problem_body(string body) =>
        Assert.Null(GatewayProblemBody.TryRead(Encoding.UTF8.GetBytes(body)));

    [Fact]
    public void The_exception_message_names_code_kind_and_status_but_no_upstream_text()
    {
        var exception = new GatewayRelayException(GatewayFailureMapper.FromResponse(HttpStatusCode.BadGateway, problem: null));

        Assert.Equal("The gateway relay call failed: GATEWAY_UPSTREAM_UNAVAILABLE, ServerError (HTTP 502).", exception.Message);
        Assert.Equal(GatewayFailureCodes.UpstreamUnavailable, new GatewayRelayException().Code);
        Assert.Equal(OutboundFailureKind.Unknown, new GatewayRelayException("m").Failure.Kind);
    }
}
