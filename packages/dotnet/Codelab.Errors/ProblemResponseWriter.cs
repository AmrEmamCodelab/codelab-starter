using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;

namespace Codelab.Errors;

/// <summary>
/// Writes an envelope through <c>IProblemDetailsService</c> (which runs <see cref="ProblemDetailsNormalizer"/>), and,
/// when no writer accepts the request (an <c>Accept</c> header without JSON), normalizes and writes it directly, so a
/// Codelab error response is never a bare status.
/// </summary>
internal static class ProblemResponseWriter
{
    public static async Task WriteAsync(HttpContext httpContext, ProblemDetails problem)
    {
        httpContext.Response.StatusCode = problem.Status ?? httpContext.Response.StatusCode;
        var context = new ProblemDetailsContext { HttpContext = httpContext, ProblemDetails = problem };

        var service = httpContext.RequestServices?.GetService<IProblemDetailsService>();
        if (service is not null && await service.TryWriteAsync(context).ConfigureAwait(false))
        {
            return;
        }

        ProblemDetailsNormalizer.Normalize(context);
        await httpContext.Response.WriteAsJsonAsync(
            problem, problem.GetType(), options: null, contentType: ErrorEnvelope.ProblemJsonMediaType).ConfigureAwait(false);
    }
}
