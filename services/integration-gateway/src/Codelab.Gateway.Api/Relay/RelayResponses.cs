using Codelab.Gateway.Api.Diagnostics;
using Codelab.Integration.Gateway;
using Microsoft.Net.Http.Headers;

namespace Codelab.Gateway.Api.Relay;

/// <summary>One relay route, as the log lines name it: the profile key and the operation (<see cref="RelayOperations"/>).</summary>
/// <param name="Profile">The profile key (<see cref="GatewayProfileKey"/>).</param>
/// <param name="Operation">The operation (<see cref="RelayOperations"/>).</param>
internal readonly record struct RelayStep(string Profile, string Operation);

/// <summary>Turns relay outcomes into responses, logging each one the same way for every profile.</summary>
internal static class RelayResponses
{
    private const string NoStoreDirective = "no-store";
    private const string NoCacheDirective = "no-cache";

    /// <summary>
    /// The refusal for <paramref name="failure"/>: its gateway code as problem+json (<see cref="GatewayProblems.For"/>),
    /// so the caller sees the code and the reference code and never the upstream's own answer.
    /// </summary>
    public static IResult Failed(ILogger logger, RelayStep step, UpstreamFailure failure)
    {
        ArgumentNullException.ThrowIfNull(failure);
        GatewayLog.RelayFailed(logger, profile: step.Profile, operation: step.Operation, failure.Code, failure.Kind, failure.UpstreamStatus);
        return GatewayProblems.For(failure.Code);
    }

    /// <summary>A refusal of a malformed request: logged with its code, then returned as it is.</summary>
    public static IResult Refused(ILogger logger, RelayStep step, IResult refusal, string errorCode)
    {
        GatewayLog.RelayRefused(logger, profile: step.Profile, operation: step.Operation, errorCode);
        return refusal;
    }

    /// <summary>The upstream's JSON document verbatim (200, <c>application/json</c>), or the refusal of its failure.</summary>
    public static IResult Json(ILogger logger, RelayStep step, UpstreamResult result)
    {
        ArgumentNullException.ThrowIfNull(result);
        if (!result.Succeeded)
        {
            return Failed(logger, step, result.Failure);
        }

        Completed(logger, step, result);
        return Results.Bytes(result.Body, GatewayMediaTypes.Json);
    }

    /// <summary>Logs a relay that reached the upstream and got a usable answer.</summary>
    public static void Completed(ILogger logger, RelayStep step, UpstreamResult result)
    {
        ArgumentNullException.ThrowIfNull(result);
        GatewayLog.RelayCompleted(logger, profile: step.Profile, operation: step.Operation, (int?)result.Status);
    }

    /// <summary>Marks a response that carries tokens as never cacheable by any intermediary.</summary>
    public static void NoStore(HttpResponse response)
    {
        ArgumentNullException.ThrowIfNull(response);
        response.Headers[HeaderNames.CacheControl] = NoStoreDirective;
        response.Headers[HeaderNames.Pragma] = NoCacheDirective;
    }
}
