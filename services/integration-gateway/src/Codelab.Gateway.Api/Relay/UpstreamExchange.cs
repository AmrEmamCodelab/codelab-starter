using System.Diagnostics.CodeAnalysis;
using System.Net;
using Codelab.Integration.Classification;
using Polly;

namespace Codelab.Gateway.Api.Relay;

/// <summary>An upstream answer the gateway can relay (<see cref="Body"/>), or why there is none (<see cref="Failure"/>).</summary>
internal sealed class UpstreamResult
{
    private UpstreamResult(HttpStatusCode? status, byte[]? body, UpstreamFailure? failure)
    {
        Status = status;
        Body = body;
        Failure = failure;
    }

    /// <summary>The upstream status, when it answered.</summary>
    public HttpStatusCode? Status { get; }

    /// <summary>The upstream body (success only).</summary>
    public byte[]? Body { get; }

    /// <summary>The failure (failure only).</summary>
    public UpstreamFailure? Failure { get; }

    /// <summary>True when the upstream answered with a success status and a body within the ceiling.</summary>
    [MemberNotNullWhen(true, nameof(Body))]
    [MemberNotNullWhen(false, nameof(Failure))]
    public bool Succeeded => Failure is null;

    public static UpstreamResult Success(HttpStatusCode status, byte[] body) => new(status, body, failure: null);

    public static UpstreamResult Failed(UpstreamFailure failure) => new((HttpStatusCode?)failure.UpstreamStatus, body: null, failure);
}

/// <summary>
/// Sends one request on an upstream client (a Codelab.Integration outbound client: certificate policy, timeouts,
/// retries of safe requests) and turns everything but a bounded success answer into an <see cref="UpstreamFailure"/>.
/// A caller cancellation is not a failure: it propagates as <see cref="OperationCanceledException"/>.
/// </summary>
internal static class UpstreamExchange
{
    /// <summary>Sends <paramref name="request"/> and reads at most <paramref name="maxResponseBytes"/> of the answer.</summary>
    public static async Task<UpstreamResult> SendAsync(HttpClient http, HttpRequestMessage request, int maxResponseBytes, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(http);
        ArgumentNullException.ThrowIfNull(request);
        try
        {
            using var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            if (OutboundFailureClassifier.Classify(response) is { } failure)
            {
                return UpstreamResult.Failed(UpstreamFailureMapper.From(failure));
            }

            if (!response.IsSuccessStatusCode)
            {
                return UpstreamResult.Failed(UpstreamFailureMapper.Unusable(response.StatusCode));
            }

            var body = await BoundedBody.ReadAsync(response.Content, maxResponseBytes, cancellationToken);
            return body is null
                ? UpstreamResult.Failed(UpstreamFailureMapper.Unusable(response.StatusCode))
                : UpstreamResult.Success(response.StatusCode, body);
        }
        catch (Exception exception) when (!cancellationToken.IsCancellationRequested && IsTransportFailure(exception))
        {
            return UpstreamResult.Failed(UpstreamFailureMapper.FromException(exception));
        }
    }

    /// <summary>A failure of the call itself (connection, TLS, timeout), as opposed to a bug in the gateway.</summary>
    internal static bool IsTransportFailure(Exception exception) =>
        exception is HttpRequestException or OperationCanceledException or ExecutionRejectedException or IOException;
}
