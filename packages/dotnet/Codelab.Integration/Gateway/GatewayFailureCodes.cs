using Microsoft.AspNetCore.Http;

namespace Codelab.Integration.Gateway;

/// <summary>
/// The problem codes the gateway answers with (problem+json member <see cref="GatewayProblemMembers.Code"/>), each
/// with its HTTP status. The relay client maps every failure, the gateway's own and a transport failure, to one of
/// them (<see cref="GatewayRelayException.Code"/>).
/// </summary>
public static class GatewayFailureCodes
{
    /// <summary>The upstream (or the gateway itself) could not be reached or timed out. 503.</summary>
    public const string UpstreamUnavailable = "GATEWAY_UPSTREAM_UNAVAILABLE";

    /// <summary>The upstream answered, but refused the request or answered something unusable. 502.</summary>
    public const string UpstreamRejected = "GATEWAY_UPSTREAM_REJECTED";

    /// <summary>The profile is registered on the gateway but not enabled in its configuration. 404.</summary>
    public const string ProfileDisabled = "GATEWAY_PROFILE_DISABLED";

    /// <summary>Every code, in a stable order.</summary>
    public static IReadOnlyList<string> All { get; } = [UpstreamUnavailable, UpstreamRejected, ProfileDisabled];

    /// <summary>True when <paramref name="code"/> is one of <see cref="All"/>.</summary>
    public static bool IsKnown(string? code) => code is not null && All.Contains(code, StringComparer.Ordinal);

    /// <summary>The HTTP status the gateway answers <paramref name="code"/> with.</summary>
    /// <exception cref="ArgumentOutOfRangeException">The code is not a gateway failure code.</exception>
    public static int StatusOf(string code) => code switch
    {
        UpstreamUnavailable => StatusCodes.Status503ServiceUnavailable,
        UpstreamRejected => StatusCodes.Status502BadGateway,
        ProfileDisabled => StatusCodes.Status404NotFound,
        _ => throw new ArgumentOutOfRangeException(nameof(code), code, "Not a gateway failure code."),
    };

    /// <summary>The neutral problem title of <paramref name="code"/> (never upstream text).</summary>
    /// <exception cref="ArgumentOutOfRangeException">The code is not a gateway failure code.</exception>
    public static string TitleOf(string code) => code switch
    {
        UpstreamUnavailable => "The upstream service is unavailable.",
        UpstreamRejected => "The upstream service refused the request.",
        ProfileDisabled => "The integration profile is not enabled.",
        _ => throw new ArgumentOutOfRangeException(nameof(code), code, "Not a gateway failure code."),
    };
}

/// <summary>The problem+json extension members the gateway and the relay client agree on.</summary>
public static class GatewayProblemMembers
{
    /// <summary>The failure code (<see cref="GatewayFailureCodes"/>).</summary>
    public const string Code = "code";

    /// <summary>The gateway's error reference code, when its error handling adds one.</summary>
    public const string ReferenceCode = "referenceCode";
}

/// <summary>Builds the gateway's problem+json refusals, so every profile answers the same shape.</summary>
public static class GatewayProblems
{
    /// <summary>
    /// The refusal for <paramref name="code"/>: its status, its neutral title and the <c>code</c> member. It goes
    /// through the host's IProblemDetailsService, so the host's error handling can add the reference code.
    /// </summary>
    public static IResult For(string code)
    {
        var extensions = new Dictionary<string, object?> { [GatewayProblemMembers.Code] = code };
        return TypedResults.Problem(statusCode: GatewayFailureCodes.StatusOf(code), title: GatewayFailureCodes.TitleOf(code), extensions: extensions);
    }
}
