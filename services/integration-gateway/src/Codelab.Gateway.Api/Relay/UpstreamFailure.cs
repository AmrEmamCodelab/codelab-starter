using System.Net;
using Codelab.Integration.Classification;
using Codelab.Integration.Gateway;

namespace Codelab.Gateway.Api.Relay;

/// <summary>
/// Why an upstream call produced no usable answer: the gateway failure code the caller receives
/// (<see cref="GatewayFailureCodes"/>), the classified kind and the upstream status when it answered. Holds no body and
/// no upstream text, so it is safe to log.
/// </summary>
/// <param name="Code">The gateway failure code (<see cref="GatewayFailureCodes"/>).</param>
/// <param name="Kind">What went wrong, from <see cref="OutboundFailureClassifier"/>.</param>
/// <param name="UpstreamStatus">The upstream's HTTP status, when it answered.</param>
internal sealed record UpstreamFailure(string Code, OutboundFailureKind Kind, int? UpstreamStatus);

/// <summary>
/// The one ruling on which gateway code an upstream failure becomes. The upstream could not be reached, timed out,
/// throttled, failed on its side or presented a certificate the policy refused: <see cref="GatewayFailureCodes.UpstreamUnavailable"/>
/// (503). The upstream answered but refused the request, or answered something the gateway cannot relay:
/// <see cref="GatewayFailureCodes.UpstreamRejected"/> (502).
/// </summary>
internal static class UpstreamFailureMapper
{
    /// <summary>The gateway code of a classified failure kind.</summary>
    public static string CodeOf(OutboundFailureKind kind) => kind switch
    {
        OutboundFailureKind.Timeout
            or OutboundFailureKind.ConnectionFailed
            or OutboundFailureKind.CertificateRejected
            or OutboundFailureKind.Throttled
            or OutboundFailureKind.ServerError
            or OutboundFailureKind.Cancelled => GatewayFailureCodes.UpstreamUnavailable,
        _ => GatewayFailureCodes.UpstreamRejected,
    };

    /// <summary>The failure a classified outbound failure stands for.</summary>
    public static UpstreamFailure From(OutboundFailure failure)
    {
        ArgumentNullException.ThrowIfNull(failure);
        return new UpstreamFailure(CodeOf(failure.Kind), failure.Kind, (int?)failure.StatusCode);
    }

    /// <summary>The failure an exception thrown by the outbound call stands for.</summary>
    public static UpstreamFailure FromException(Exception exception) => From(OutboundFailureClassifier.Classify(exception));

    /// <summary>
    /// The upstream answered with <paramref name="status"/> but the answer cannot be relayed: not a success (a redirect
    /// is never followed), too large, or not the expected JSON.
    /// </summary>
    public static UpstreamFailure Unusable(HttpStatusCode status) =>
        new(GatewayFailureCodes.UpstreamRejected, OutboundFailureKind.Unknown, (int)status);
}
