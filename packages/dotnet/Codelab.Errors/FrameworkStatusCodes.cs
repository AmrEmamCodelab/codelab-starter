using Microsoft.AspNetCore.Http;

namespace Codelab.Errors;

/// <summary>A status's code and vetted English fallback sentence.</summary>
/// <param name="Code">The machine code.</param>
/// <param name="Detail">The English fallback sentence; never quotes a value.</param>
public readonly record struct StatusDescription(string Code, string Detail);

/// <summary>
/// The single status-to-(code, sentence) table. Every source that labels a status by itself resolves through
/// <see cref="Describe"/>: the status-code pages (a 401, a routing 404, a 405 or 415 written with an empty body), the
/// problem-details customization (MVC's client-error mapping and <c>Results.Problem</c>), the request-body
/// classifier and <see cref="CodedRefusal"/>. One place to change when a status needs a different code or sentence.
/// </summary>
public static class FrameworkStatusCodes
{
    /// <summary>The first status a server fault has.</summary>
    public const int FirstServerErrorStatus = StatusCodes.Status500InternalServerError;

    private static readonly StatusDescription UnexpectedServerError = new(ErrorCodes.InternalError, "An unexpected error occurred.");
    private static readonly StatusDescription UnprocessableRequest = new(ErrorCodes.Malformed, "The request could not be processed.");

    private static readonly Dictionary<int, StatusDescription> Table = new()
    {
        [StatusCodes.Status400BadRequest] = UnprocessableRequest,
        [StatusCodes.Status401Unauthorized] = new(ErrorCodes.Unauthenticated, "Authentication is required."),
        [StatusCodes.Status403Forbidden] = new(ErrorCodes.Forbidden, "You do not have permission to do this."),
        [StatusCodes.Status404NotFound] = new(ErrorCodes.NotFound, "The requested resource does not exist."),
        [StatusCodes.Status405MethodNotAllowed] = new(ErrorCodes.Malformed, "That method is not allowed on this resource."),
        [StatusCodes.Status406NotAcceptable] = new(ErrorCodes.Malformed, "No acceptable response format was offered."),
        [StatusCodes.Status409Conflict] = new(ErrorCodes.Conflict, "The request conflicts with the current state of the resource."),
        [StatusCodes.Status413PayloadTooLarge] = new(ErrorCodes.PayloadTooLarge, "The request body is too large."),
        [StatusCodes.Status415UnsupportedMediaType] = new(ErrorCodes.Malformed, "That content type is not supported."),
        [StatusCodes.Status422UnprocessableEntity] = new(ErrorCodes.BusinessRule, "The request was understood but cannot be accepted."),
        [StatusCodes.Status429TooManyRequests] = new(ErrorCodes.Retryable, "Too many requests. Please try again in a moment."),
        [StatusCodes.Status502BadGateway] = new(ErrorCodes.Upstream, "A dependency is temporarily unavailable."),
        [StatusCodes.Status503ServiceUnavailable] = new(ErrorCodes.Retryable, "The system was busy. Please try again in a moment."),
    };

    /// <summary>
    /// The code and sentence for <paramref name="status"/>: the table row, else <see cref="ErrorCodes.InternalError"/> for
    /// any other 5xx, else <see cref="ErrorCodes.Malformed"/>.
    /// </summary>
    public static StatusDescription Describe(int status)
    {
        if (Table.TryGetValue(status, out var description))
        {
            return description;
        }

        return status >= FirstServerErrorStatus ? UnexpectedServerError : UnprocessableRequest;
    }

    /// <summary>True when <paramref name="detail"/> is one of the table's own sentences (so it is safe on the wire).</summary>
    internal static bool IsTableSentence(string? detail) =>
        detail is not null
        && (Table.Values.Any(d => string.Equals(d.Detail, detail, StringComparison.Ordinal))
            || string.Equals(UnexpectedServerError.Detail, detail, StringComparison.Ordinal));
}
