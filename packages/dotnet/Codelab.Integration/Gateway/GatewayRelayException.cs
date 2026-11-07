using Codelab.Integration.Classification;

namespace Codelab.Integration.Gateway;

/// <summary>
/// A relay call failed. <see cref="Code"/> is a <see cref="GatewayFailureCodes"/> value (the gateway's own code when
/// it sent one); <see cref="Failure"/> is the outbound classification of what happened (status, kind, transient).
/// The message never contains upstream response text.
/// </summary>
public sealed class GatewayRelayException : Exception
{
    /// <summary>Creates the exception with the default code and an unknown failure.</summary>
    public GatewayRelayException()
        : this("The gateway relay call failed.")
    {
    }

    /// <summary>Creates the exception with the default code and an unknown failure.</summary>
    public GatewayRelayException(string message)
        : this(message, innerException: null)
    {
    }

    /// <summary>Creates the exception with the default code and an unknown failure.</summary>
    public GatewayRelayException(string message, Exception? innerException)
        : base(message, innerException)
    {
        Code = GatewayFailureCodes.UpstreamUnavailable;
        Failure = new OutboundFailure(OutboundFailureKind.Unknown, StatusCode: null, IsTransient: false);
    }

    /// <summary>Creates the exception for a classified failure.</summary>
    public GatewayRelayException(GatewayRelayFailure failure)
        : this(failure, innerException: null)
    {
    }

    /// <summary>Creates the exception for a classified failure caused by <paramref name="innerException"/>.</summary>
    public GatewayRelayException(GatewayRelayFailure failure, Exception? innerException)
        : base(DescribeOrThrow(failure), innerException)
    {
        Code = failure.Code;
        Failure = failure.Outbound;
        ReferenceCode = failure.ReferenceCode;
    }

    /// <summary>The gateway failure code (<see cref="GatewayFailureCodes"/>).</summary>
    public string Code { get; }

    /// <summary>The outbound classification of the failure.</summary>
    public OutboundFailure Failure { get; }

    /// <summary>The gateway's error reference code, when its refusal carried one.</summary>
    public string? ReferenceCode { get; }

    private static string DescribeOrThrow(GatewayRelayFailure failure)
    {
        ArgumentNullException.ThrowIfNull(failure);
        var status = failure.Outbound.StatusCode is { } code ? $" (HTTP {(int)code})" : string.Empty;
        return $"The gateway relay call failed: {failure.Code}, {failure.Outbound.Kind}{status}.";
    }
}

/// <summary>A classified relay failure: the gateway code, the outbound failure and the gateway's reference code.</summary>
/// <param name="Code">The gateway failure code.</param>
/// <param name="Outbound">The outbound classification.</param>
/// <param name="ReferenceCode">The gateway's reference code, if any.</param>
public sealed record GatewayRelayFailure(string Code, OutboundFailure Outbound, string? ReferenceCode);
