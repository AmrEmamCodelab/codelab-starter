using System.Net;
using System.Security.Authentication;
using Codelab.Gateway.Api.Relay;
using Codelab.Integration.Classification;
using Codelab.Integration.Gateway;
using Polly.Timeout;

namespace Codelab.Gateway.Api.Tests.Unit;

public sealed class BoundedBodyTests
{
    [Theory]
    [InlineData(0, 0)]
    [InlineData(10, 10)]
    [InlineData(20000, 20000)]
    public async Task A_body_within_the_ceiling_is_read_whole(int length, int ceiling)
    {
        using var stream = new MemoryStream(new byte[length]);

        var body = await BoundedBody.ReadAsync(stream, ceiling, CancellationToken.None);

        Assert.Equal(length, body!.Length);
    }

    [Fact]
    public async Task A_body_above_the_ceiling_is_null()
    {
        using var stream = new MemoryStream(new byte[11]);

        Assert.Null(await BoundedBody.ReadAsync(stream, maxBytes: 10, CancellationToken.None));
    }

    [Fact]
    public async Task Content_declaring_a_length_above_the_ceiling_is_null_without_reading()
    {
        using var content = new ByteArrayContent(new byte[64]);

        Assert.Null(await BoundedBody.ReadAsync(content, maxBytes: 32, CancellationToken.None));
    }
}

public sealed class UpstreamFailureMapperTests
{
    [Theory]
    [InlineData(OutboundFailureKind.Timeout, GatewayFailureCodes.UpstreamUnavailable)]
    [InlineData(OutboundFailureKind.ConnectionFailed, GatewayFailureCodes.UpstreamUnavailable)]
    [InlineData(OutboundFailureKind.CertificateRejected, GatewayFailureCodes.UpstreamUnavailable)]
    [InlineData(OutboundFailureKind.Throttled, GatewayFailureCodes.UpstreamUnavailable)]
    [InlineData(OutboundFailureKind.ServerError, GatewayFailureCodes.UpstreamUnavailable)]
    [InlineData(OutboundFailureKind.Cancelled, GatewayFailureCodes.UpstreamUnavailable)]
    [InlineData(OutboundFailureKind.Unauthorized, GatewayFailureCodes.UpstreamRejected)]
    [InlineData(OutboundFailureKind.ClientError, GatewayFailureCodes.UpstreamRejected)]
    [InlineData(OutboundFailureKind.Unknown, GatewayFailureCodes.UpstreamRejected)]
    public void Each_failure_kind_has_one_gateway_code(OutboundFailureKind kind, string code) =>
        Assert.Equal(code, UpstreamFailureMapper.CodeOf(kind));

    [Fact]
    public void A_tls_refusal_is_unavailable_and_keeps_its_kind()
    {
        var failure = UpstreamFailureMapper.FromException(new HttpRequestException("handshake", new AuthenticationException("refused")));

        Assert.Equal(new UpstreamFailure(GatewayFailureCodes.UpstreamUnavailable, OutboundFailureKind.CertificateRejected, UpstreamStatus: null), failure);
    }

    [Fact]
    public void An_unusable_answer_is_rejected_with_its_status()
    {
        Assert.Equal(new UpstreamFailure(GatewayFailureCodes.UpstreamRejected, OutboundFailureKind.Unknown, 302), UpstreamFailureMapper.Unusable(HttpStatusCode.Found));
    }
}

public sealed class UpstreamExchangeTests
{
    private static readonly Uri Target = new("https://upstream.example.test/x");

    [Fact]
    public async Task A_success_answer_within_the_ceiling_is_returned()
    {
        var result = await SendAsync(_ => Answer(HttpStatusCode.OK, "{}"));

        Assert.True(result.Succeeded);
        Assert.Equal(HttpStatusCode.OK, result.Status);
        Assert.Equal("{}"u8.ToArray(), result.Body);
    }

    [Theory]
    [InlineData(HttpStatusCode.BadRequest, GatewayFailureCodes.UpstreamRejected)]
    [InlineData(HttpStatusCode.Unauthorized, GatewayFailureCodes.UpstreamRejected)]
    [InlineData(HttpStatusCode.ServiceUnavailable, GatewayFailureCodes.UpstreamUnavailable)]
    [InlineData(HttpStatusCode.Found, GatewayFailureCodes.UpstreamRejected)]
    public async Task A_status_that_is_not_a_success_is_a_failure(HttpStatusCode status, string code)
    {
        var result = await SendAsync(_ => Answer(status, "{}"));

        Assert.False(result.Succeeded);
        Assert.Equal(code, result.Failure.Code);
        Assert.Equal((int)status, result.Failure.UpstreamStatus);
    }

    [Fact]
    public async Task An_answer_above_the_ceiling_is_rejected()
    {
        var result = await SendAsync(_ => Answer(HttpStatusCode.OK, new string('x', 100)), maxBytes: 10);

        Assert.Equal(GatewayFailureCodes.UpstreamRejected, result.Failure!.Code);
    }

    [Fact]
    public async Task A_connection_failure_is_unavailable()
    {
        var result = await SendAsync(_ => throw new HttpRequestException(HttpRequestError.ConnectionError, "refused"));

        Assert.Equal(new UpstreamFailure(GatewayFailureCodes.UpstreamUnavailable, OutboundFailureKind.ConnectionFailed, UpstreamStatus: null), result.Failure);
    }

    [Fact]
    public async Task A_policy_timeout_is_unavailable()
    {
        var result = await SendAsync(_ => throw new TimeoutRejectedException());

        Assert.Equal(OutboundFailureKind.Timeout, result.Failure!.Kind);
        Assert.Equal(GatewayFailureCodes.UpstreamUnavailable, result.Failure.Code);
    }

    [Fact]
    public async Task A_caller_cancellation_propagates()
    {
        using var cancelled = new CancellationTokenSource();
        await cancelled.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => SendAsync(_ => Answer(HttpStatusCode.OK, "{}"), cancellationToken: cancelled.Token));
    }

    [Fact]
    public async Task A_bug_is_not_turned_into_an_upstream_failure()
    {
        await Assert.ThrowsAsync<InvalidOperationException>(() => SendAsync(_ => throw new InvalidOperationException("bug")));
    }

    private static HttpResponseMessage Answer(HttpStatusCode status, string body) => new(status) { Content = new StringContent(body) };

    private static async Task<UpstreamResult> SendAsync(Func<HttpRequestMessage, HttpResponseMessage> answer, int maxBytes = 1024, CancellationToken cancellationToken = default)
    {
        using var http = new HttpClient(new StubHandler(answer));
        using var request = new HttpRequestMessage(HttpMethod.Get, Target);
        return await UpstreamExchange.SendAsync(http, request, maxBytes, cancellationToken);
    }
}

public sealed class UpstreamAddressTests
{
    [Theory]
    [InlineData("https://idp.example.test/token", false, true)]
    [InlineData("http://idp.example.test/token", false, false)]
    [InlineData("http://localhost:5190/token", true, true)]
    [InlineData("ftp://idp.example.test/token", true, false)]
    [InlineData("https://operator@idp.example.test/token", false, false)]
    [InlineData("https://idp.example.test/token#frag", false, false)]
    public void Only_an_absolute_https_address_is_usable_outside_development(string address, bool isDevelopment, bool usable)
    {
        var problem = UpstreamAddress.Problem(new Uri(address), "Gateway:Sso:TokenEndpoint", isDevelopment);

        Assert.Equal(usable, problem is null);
    }

    [Fact]
    public void A_missing_address_names_the_setting()
    {
        Assert.Contains("Gateway:Sso:TokenEndpoint", UpstreamAddress.Problem(address: null, "Gateway:Sso:TokenEndpoint", isDevelopment: false), StringComparison.Ordinal);
    }

    [Fact]
    public void Redirects_are_switched_off_on_the_primary_handler()
    {
        using var handler = new SocketsHttpHandler();

        UpstreamClients.NeverFollowRedirects(handler);

        Assert.False(handler.AllowAutoRedirect);
    }
}

/// <summary>An HTTP handler that answers from a delegate.</summary>
internal sealed class StubHandler(Func<HttpRequestMessage, HttpResponseMessage> answer) : HttpMessageHandler
{
    public List<HttpRequestMessage> Requests { get; } = [];

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Requests.Add(request);
        return Task.FromResult(answer(request));
    }
}
