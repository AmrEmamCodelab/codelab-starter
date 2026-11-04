using System.Net;
using System.Security.Authentication;
using Polly.Timeout;

namespace Codelab.Integration.Classification;

/// <summary>
/// Classifies outbound failures, so the retry policy and the host's error mapping share one ruling on what is
/// transient. The retry policy uses it; hosts use it to map an outbound failure to their own error codes.
/// </summary>
public static class OutboundFailureClassifier
{
    private const int ClientErrorFloor = 400;
    private const int ServerErrorFloor = 500;
    private const int ServerErrorCeiling = 600;

    private static readonly HashSet<HttpStatusCode> TransientServerErrors =
    [
        HttpStatusCode.InternalServerError,
        HttpStatusCode.BadGateway,
        HttpStatusCode.ServiceUnavailable,
        HttpStatusCode.GatewayTimeout,
    ];

    /// <summary>The failure an upstream status code stands for, or null for a success or redirect status.</summary>
    public static OutboundFailure? Classify(HttpStatusCode statusCode)
    {
        var status = (int)statusCode;
        if (status < ClientErrorFloor || status >= ServerErrorCeiling)
        {
            return null;
        }

        if (status >= ServerErrorFloor)
        {
            return new OutboundFailure(OutboundFailureKind.ServerError, statusCode, TransientServerErrors.Contains(statusCode));
        }

        return statusCode switch
        {
            HttpStatusCode.RequestTimeout => new OutboundFailure(OutboundFailureKind.Timeout, statusCode, IsTransient: true),
            HttpStatusCode.TooManyRequests => new OutboundFailure(OutboundFailureKind.Throttled, statusCode, IsTransient: true),
            HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden =>
                new OutboundFailure(OutboundFailureKind.Unauthorized, statusCode, IsTransient: false),
            _ => new OutboundFailure(OutboundFailureKind.ClientError, statusCode, IsTransient: false),
        };
    }

    /// <summary>The failure a response stands for, or null when it succeeded.</summary>
    public static OutboundFailure? Classify(HttpResponseMessage response)
    {
        ArgumentNullException.ThrowIfNull(response);
        return Classify(response.StatusCode);
    }

    /// <summary>The failure an exception thrown by an outbound call stands for.</summary>
    public static OutboundFailure Classify(Exception exception)
    {
        ArgumentNullException.ThrowIfNull(exception);
        return exception switch
        {
            TimeoutRejectedException => Failure(OutboundFailureKind.Timeout, isTransient: true),
            TaskCanceledException { InnerException: TimeoutException } => Failure(OutboundFailureKind.Timeout, isTransient: true),
            OperationCanceledException => Failure(OutboundFailureKind.Cancelled, isTransient: false),
            HttpRequestException request => ClassifyRequestException(request),
            _ => Failure(OutboundFailureKind.Unknown, isTransient: false),
        };
    }

    private static OutboundFailure ClassifyRequestException(HttpRequestException exception)
    {
        if (exception.HttpRequestError == HttpRequestError.SecureConnectionError || HasAuthenticationFailure(exception))
        {
            return Failure(OutboundFailureKind.CertificateRejected, isTransient: false);
        }

        return exception.HttpRequestError switch
        {
            HttpRequestError.NameResolutionError or HttpRequestError.ConnectionError or HttpRequestError.ProxyTunnelError
                or HttpRequestError.ResponseEnded => Failure(OutboundFailureKind.ConnectionFailed, isTransient: true),
            HttpRequestError.Unknown when exception.InnerException is IOException =>
                Failure(OutboundFailureKind.ConnectionFailed, isTransient: true),
            _ => Failure(OutboundFailureKind.Unknown, isTransient: false),
        };
    }

    private static bool HasAuthenticationFailure(Exception exception)
    {
        for (var current = exception.InnerException; current is not null; current = current.InnerException)
        {
            if (current is AuthenticationException)
            {
                return true;
            }
        }

        return false;
    }

    private static OutboundFailure Failure(OutboundFailureKind kind, bool isTransient) => new(kind, StatusCode: null, isTransient);
}
