using System.Net;
using System.Security.Authentication;
using Codelab.Integration.Classification;
using Polly.Timeout;

namespace Codelab.Integration.Tests.Classification;

public sealed class OutboundFailureClassifierTests
{
    [Theory]
    [InlineData(HttpStatusCode.OK)]
    [InlineData(HttpStatusCode.NoContent)]
    [InlineData(HttpStatusCode.Redirect)]
    [InlineData(HttpStatusCode.NotModified)]
    public void Success_and_redirect_statuses_are_not_failures(HttpStatusCode status) =>
        Assert.Null(OutboundFailureClassifier.Classify(status));

    [Theory]
    [InlineData(HttpStatusCode.RequestTimeout, OutboundFailureKind.Timeout, true)]
    [InlineData(HttpStatusCode.TooManyRequests, OutboundFailureKind.Throttled, true)]
    [InlineData(HttpStatusCode.Unauthorized, OutboundFailureKind.Unauthorized, false)]
    [InlineData(HttpStatusCode.Forbidden, OutboundFailureKind.Unauthorized, false)]
    [InlineData(HttpStatusCode.BadRequest, OutboundFailureKind.ClientError, false)]
    [InlineData(HttpStatusCode.NotFound, OutboundFailureKind.ClientError, false)]
    [InlineData(HttpStatusCode.Conflict, OutboundFailureKind.ClientError, false)]
    [InlineData(HttpStatusCode.InternalServerError, OutboundFailureKind.ServerError, true)]
    [InlineData(HttpStatusCode.BadGateway, OutboundFailureKind.ServerError, true)]
    [InlineData(HttpStatusCode.ServiceUnavailable, OutboundFailureKind.ServerError, true)]
    [InlineData(HttpStatusCode.GatewayTimeout, OutboundFailureKind.ServerError, true)]
    [InlineData(HttpStatusCode.NotImplemented, OutboundFailureKind.ServerError, false)]
    [InlineData(HttpStatusCode.HttpVersionNotSupported, OutboundFailureKind.ServerError, false)]
    public void Error_statuses_are_classified(HttpStatusCode status, OutboundFailureKind kind, bool transient)
    {
        var failure = OutboundFailureClassifier.Classify(status);

        Assert.Equal(new OutboundFailure(kind, status, transient), failure);
    }

    [Fact]
    public void A_status_outside_the_http_range_is_not_a_failure() =>
        Assert.Null(OutboundFailureClassifier.Classify((HttpStatusCode)600));

    [Fact]
    public void A_response_is_classified_by_its_status()
    {
        using var response = new HttpResponseMessage(HttpStatusCode.TooManyRequests);

        Assert.Equal(OutboundFailureKind.Throttled, OutboundFailureClassifier.Classify(response)!.Kind);
    }

    public static TheoryData<Exception, OutboundFailureKind, bool> Exceptions() => new()
    {
        { new TimeoutRejectedException(), OutboundFailureKind.Timeout, true },
        { new TaskCanceledException("client timeout", new TimeoutException()), OutboundFailureKind.Timeout, true },
        { new OperationCanceledException(), OutboundFailureKind.Cancelled, false },
        { new TaskCanceledException(), OutboundFailureKind.Cancelled, false },
        { new HttpRequestException(HttpRequestError.SecureConnectionError, "tls"), OutboundFailureKind.CertificateRejected, false },
        { new HttpRequestException("tls", new AuthenticationException("remote certificate invalid")), OutboundFailureKind.CertificateRejected, false },
        { new HttpRequestException(HttpRequestError.ConnectionError, "refused"), OutboundFailureKind.ConnectionFailed, true },
        { new HttpRequestException(HttpRequestError.NameResolutionError, "dns"), OutboundFailureKind.ConnectionFailed, true },
        { new HttpRequestException(HttpRequestError.ResponseEnded, "reset"), OutboundFailureKind.ConnectionFailed, true },
        { new HttpRequestException("broken pipe", new IOException("reset")), OutboundFailureKind.ConnectionFailed, true },
        { new HttpRequestException(HttpRequestError.InvalidResponse, "garbage"), OutboundFailureKind.Unknown, false },
        { new InvalidOperationException(), OutboundFailureKind.Unknown, false },
    };

    [Theory]
    [MemberData(nameof(Exceptions))]
    public void Exceptions_are_classified(Exception exception, OutboundFailureKind kind, bool transient)
    {
        var failure = OutboundFailureClassifier.Classify(exception);

        Assert.Equal(new OutboundFailure(kind, StatusCode: null, transient), failure);
    }

    [Fact]
    public void The_failure_kinds_keep_their_stored_values()
    {
        int[] expected = [0, 1, 2, 3, 4, 5, 6, 7, 8];

        Assert.Equal(expected, Enum.GetValues<OutboundFailureKind>().Select(kind => (int)kind).Order());
        Assert.Equal(3, (int)OutboundFailureKind.CertificateRejected);
    }
}
