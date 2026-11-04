using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace Codelab.Errors;

/// <summary>
/// A result that writes one envelope, from a minimal API (<see cref="IResult"/>) or an MVC action
/// (<see cref="IActionResult"/>), through <c>IProblemDetailsService</c> so the normalizer and scrubber always run, with
/// a direct problem+json fallback when no writer accepts the request.
/// </summary>
public sealed class ProblemEnvelopeResult : IResult, IActionResult, IStatusCodeHttpResult, IValueHttpResult<ProblemDetails>, IContentTypeHttpResult
{
    /// <summary>A result for <paramref name="problem"/>, which must carry a status.</summary>
    public ProblemEnvelopeResult(ProblemDetails problem)
    {
        ArgumentNullException.ThrowIfNull(problem);
        if (problem.Status is null)
        {
            throw new ArgumentException("The problem details must carry a status.", nameof(problem));
        }

        Value = problem;
    }

    /// <summary>The body.</summary>
    public ProblemDetails Value { get; }

    /// <inheritdoc />
    public int? StatusCode => Value.Status;

    /// <inheritdoc />
    public string ContentType => ErrorEnvelope.ProblemJsonMediaType;

    /// <inheritdoc />
    public Task ExecuteAsync(HttpContext httpContext)
    {
        ArgumentNullException.ThrowIfNull(httpContext);
        return ProblemResponseWriter.WriteAsync(httpContext, Value);
    }

    /// <inheritdoc />
    public Task ExecuteResultAsync(ActionContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        return ProblemResponseWriter.WriteAsync(context.HttpContext, Value);
    }
}
