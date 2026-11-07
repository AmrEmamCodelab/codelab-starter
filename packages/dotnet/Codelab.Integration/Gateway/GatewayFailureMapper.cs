using System.Net;
using System.Text.Json;
using Codelab.Integration.Classification;
using Polly.Timeout;

namespace Codelab.Integration.Gateway;

/// <summary>
/// Maps what went wrong with a relay call to a <see cref="GatewayRelayFailure"/>, through
/// <see cref="OutboundFailureClassifier"/>: the gateway's own code wins; otherwise a refusal of the request (4xx other
/// than 408 and 429) is <see cref="GatewayFailureCodes.UpstreamRejected"/>, and everything else (no connection, a
/// timeout, a refused certificate, a 5xx, a throttle) is <see cref="GatewayFailureCodes.UpstreamUnavailable"/>.
/// </summary>
internal static class GatewayFailureMapper
{
    /// <summary>The failure of a non-success answer, with the gateway's problem body when it sent one.</summary>
    internal static GatewayRelayFailure FromResponse(HttpStatusCode status, GatewayProblemBody? problem)
    {
        var outbound = OutboundFailureClassifier.Classify(status) ?? new OutboundFailure(OutboundFailureKind.Unknown, status, IsTransient: false);
        var code = GatewayFailureCodes.IsKnown(problem?.Code) ? problem!.Code! : CodeFor(outbound);
        return new GatewayRelayFailure(code, outbound, problem?.ReferenceCode);
    }

    /// <summary>The failure of a transport exception (see <see cref="IsTransportFailure"/>).</summary>
    internal static GatewayRelayFailure FromException(Exception exception)
    {
        var outbound = OutboundFailureClassifier.Classify(exception);
        return new GatewayRelayFailure(CodeFor(outbound), outbound, ReferenceCode: null);
    }

    /// <summary>A success answer whose body is unusable (too large, not JSON, not the expected shape).</summary>
    internal static GatewayRelayFailure Unusable(HttpStatusCode status) =>
        new(GatewayFailureCodes.UpstreamRejected, new OutboundFailure(OutboundFailureKind.Unknown, status, IsTransient: false), ReferenceCode: null);

    /// <summary>
    /// True for a failure of the call itself (connection, TLS, timeout, an aborted read), not for a caller
    /// cancellation and not for a programming error, which must surface as they are.
    /// </summary>
    internal static bool IsTransportFailure(Exception exception, CancellationToken callerToken) => exception switch
    {
        OperationCanceledException when callerToken.IsCancellationRequested => false,
        HttpRequestException or TimeoutRejectedException or OperationCanceledException or IOException => true,
        _ => false,
    };

    internal static string CodeFor(OutboundFailure failure) =>
        failure.Kind is OutboundFailureKind.ClientError or OutboundFailureKind.Unauthorized
            ? GatewayFailureCodes.UpstreamRejected
            : GatewayFailureCodes.UpstreamUnavailable;
}

/// <summary>The members of a gateway problem body the client reads.</summary>
/// <param name="Code">The <c>code</c> member.</param>
/// <param name="ReferenceCode">The <c>referenceCode</c> member.</param>
internal sealed record GatewayProblemBody(string? Code, string? ReferenceCode)
{
    /// <summary>Reads the body as a problem object, or null when it is not a JSON object.</summary>
    internal static GatewayProblemBody? TryRead(ReadOnlySpan<byte> body)
    {
        if (body.IsEmpty)
        {
            return null;
        }

        try
        {
            var reader = new Utf8JsonReader(body);
            using var document = JsonDocument.ParseValue(ref reader);
            var root = document.RootElement;
            return root.ValueKind == JsonValueKind.Object
                ? new GatewayProblemBody(StringMember(root, GatewayProblemMembers.Code), StringMember(root, GatewayProblemMembers.ReferenceCode))
                : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static string? StringMember(JsonElement root, string name) =>
        root.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;
}
