using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace Codelab.Errors;

/// <summary>
/// The last guard before an error body reaches the client: no exception text, type name, framework id or ad-hoc member
/// leaves the server, whichever writer produced the body (the exception handler, MVC's <c>Problem(detail: ex.Message)</c>,
/// <c>Results.Problem</c>, the developer exception page). Applied to every body by the problem-details
/// customization (<see cref="ProblemDetailsNormalizer"/>).
/// <list type="bullet">
/// <item><c>title</c> is <see cref="ErrorEnvelope.Title"/>; <c>type</c> and <c>instance</c> are removed.</item>
/// <item><c>detail</c> survives only when a Codelab writer vetted it (<see cref="MarkVetted"/>) or it is a status-table
///   sentence; anything else is replaced by the status's sentence (<see cref="FrameworkStatusCodes.Describe"/>).</item>
/// <item>Extension members other than <c>code</c>, <c>correlationId</c>, <c>referenceCode</c>, <c>errors</c> and the
///   host's <see cref="CodelabErrorsOptions.AllowedExtensionMembers"/> are removed.</item>
/// </list>
/// </summary>
public static class ClientErrorScrubber
{
    private const string VettedDetailItemKey = "__codelab_vetted_problem_detail";

    /// <summary>The extension members every envelope may carry.</summary>
    public static IReadOnlySet<string> EnvelopeMembers { get; } = new HashSet<string>(StringComparer.Ordinal)
    {
        ErrorEnvelope.CodeMember,
        ErrorEnvelope.CorrelationIdMember,
        ErrorEnvelope.ReferenceCodeMember,
        ErrorEnvelope.ErrorsMember,
    };

    /// <summary>Scrubs <paramref name="problem"/> in place (see the class remarks).</summary>
    public static void Scrub(ProblemDetails problem, HttpContext httpContext, IReadOnlySet<string> allowedExtensionMembers)
    {
        ArgumentNullException.ThrowIfNull(problem);
        ArgumentNullException.ThrowIfNull(httpContext);
        ArgumentNullException.ThrowIfNull(allowedExtensionMembers);

        problem.Type = null;
        problem.Instance = null;
        problem.Title = ErrorEnvelope.Title;

        if (!IsVetted(problem.Detail, httpContext))
        {
            problem.Detail = FrameworkStatusCodes.Describe(problem.Status ?? httpContext.Response.StatusCode).Detail;
        }

        var unexpected = problem.Extensions.Keys
            .Where(member => !EnvelopeMembers.Contains(member) && !allowedExtensionMembers.Contains(member))
            .ToArray();
        foreach (var member in unexpected)
        {
            problem.Extensions.Remove(member);
        }
    }

    /// <summary>Records <paramref name="detail"/> as a sentence a Codelab writer vetted for the wire on this request.</summary>
    public static void MarkVetted(HttpContext httpContext, string detail)
    {
        ArgumentNullException.ThrowIfNull(httpContext);
        ArgumentNullException.ThrowIfNull(detail);

        httpContext.Items[VettedDetailItemKey] = detail;
    }

    private static bool IsVetted(string? detail, HttpContext httpContext) =>
        detail is not null
        && ((httpContext.Items.TryGetValue(VettedDetailItemKey, out var vetted)
                && string.Equals(vetted as string, detail, StringComparison.Ordinal))
            || FrameworkStatusCodes.IsTableSentence(detail));
}
