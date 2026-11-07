using Codelab.Errors;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Net.Http.Headers;

namespace Codelab.Gateway.Api.Relay;

/// <summary>
/// The refusals of a malformed relay request, as the one Codelab.Errors envelope (problem+json with <c>code</c>,
/// <c>correlationId</c> and <c>referenceCode</c>). Upstream failures are not here: they are
/// <c>GatewayProblems.For</c> with a gateway failure code.
/// </summary>
internal static class RelayRefusals
{
    /// <summary>The member of <see cref="Invalid"/>'s errors that names the request body.</summary>
    public const string BodyMember = "body";

    /// <summary>400 <see cref="ErrorCodes.Malformed"/>: the wrong content type or a body that does not parse.</summary>
    public static IResult Malformed(string reason) =>
        CodedRefusal.Invalid(new Dictionary<string, string[]> { [BodyMember] = [reason] }, ErrorCodes.Malformed).ToResult();

    /// <summary>400 <see cref="ErrorCodes.Validation"/> with every problem of the body.</summary>
    public static IResult Invalid(IReadOnlyList<string> problems) =>
        CodedRefusal.Invalid(new Dictionary<string, string[]> { [BodyMember] = [.. problems] }).ToResult();

    /// <summary>413 <see cref="ErrorCodes.PayloadTooLarge"/>: the body is above the relay ceiling.</summary>
    public static IResult TooLarge()
    {
        var problem = new ProblemDetails
        {
            Status = StatusCodes.Status413PayloadTooLarge,
            Title = ErrorEnvelope.Title,
            Detail = FrameworkStatusCodes.Describe(StatusCodes.Status413PayloadTooLarge).Detail,
        };
        problem.Extensions[ErrorEnvelope.CodeMember] = ErrorCodes.PayloadTooLarge;
        return new ProblemEnvelopeResult(problem);
    }

    /// <summary>True when the request's content type is <paramref name="mediaType"/> (parameters such as charset ignored).</summary>
    public static bool HasMediaType(HttpRequest request, string mediaType)
    {
        ArgumentNullException.ThrowIfNull(request);
        return MediaTypeHeaderValue.TryParse(request.ContentType, out var parsed)
            && string.Equals(parsed.MediaType.Value, mediaType, StringComparison.OrdinalIgnoreCase);
    }
}
