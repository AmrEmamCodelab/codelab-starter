using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Codelab.Errors;

/// <summary>
/// The problem-details customization (<c>ProblemDetailsOptions.CustomizeProblemDetails</c>) that brings EVERY body
/// written through <c>IProblemDetailsService</c> or MVC's problem-details factory into the one envelope: it stamps the
/// ids (<see cref="ErrorEnvelope.StampIdentity"/>), labels a body that arrived without a <c>code</c>
/// (<see cref="ErrorCodes.Validation"/> for a validation body with errors, otherwise the status table's code), and
/// scrubs it (<see cref="ClientErrorScrubber"/>). A body that already carries a code keeps it. Idempotent.
/// </summary>
public static class ProblemDetailsNormalizer
{
    /// <summary>Normalizes <paramref name="context"/>'s problem details in place.</summary>
    public static void Normalize(ProblemDetailsContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        var problem = context.ProblemDetails;
        var httpContext = context.HttpContext;
        problem.Status ??= httpContext.Response.StatusCode;

        ErrorEnvelope.StampIdentity(problem.Extensions, httpContext);
        if (!problem.Extensions.TryGetValue(ErrorEnvelope.CodeMember, out var code) || code is null)
        {
            problem.Extensions[ErrorEnvelope.CodeMember] = DefaultCode(problem);
        }

        ClientErrorScrubber.Scrub(problem, httpContext, AllowedMembers(httpContext));
    }

    private static string DefaultCode(ProblemDetails problem) =>
        problem is HttpValidationProblemDetails { Errors.Count: > 0 }
            ? ErrorCodes.Validation
            : FrameworkStatusCodes.Describe(problem.Status!.Value).Code;

    private static HashSet<string> AllowedMembers(HttpContext httpContext)
    {
        var configured = httpContext.RequestServices?.GetService<IOptions<CodelabErrorsOptions>>()?.Value.AllowedExtensionMembers;
        return configured is null ? new HashSet<string>(StringComparer.Ordinal) : new HashSet<string>(configured, StringComparer.Ordinal);
    }
}
