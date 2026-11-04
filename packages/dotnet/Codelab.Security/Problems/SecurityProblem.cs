using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Net.Http.Headers;

namespace Codelab.Security.Problems;

/// <summary>
/// A refusal written by a security control as problem+json with a stable <c>code</c>. When the host registered
/// <see cref="IProblemDetailsService"/> (Codelab.Errors does, adding <c>correlationId</c> and <c>referenceCode</c>),
/// the response goes through it, so the envelope is the host's one envelope; otherwise a minimal problem+json is written.
/// </summary>
public sealed record SecurityProblem(int Status, string Title, string? Code)
{
    /// <summary>Seconds a client should wait before retrying a 503 from a security store.</summary>
    public int? RetryAfterSeconds { get; init; }
}

/// <summary>Writes <see cref="SecurityProblem"/> responses (the one writer every control uses).</summary>
public static class SecurityProblemWriter
{
    /// <summary>The problem+json media type.</summary>
    public const string ProblemContentType = "application/problem+json";

    /// <summary>The extension member that carries the stable code.</summary>
    public const string CodeMember = "code";

    private const string TypeBase = "https://tools.ietf.org/html/rfc9110#section-15.";

    /// <summary>Writes <paramref name="problem"/> unless the response has already started (then returns false).</summary>
    public static async Task<bool> TryWriteAsync(HttpContext context, SecurityProblem problem)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(problem);
        if (context.Response.HasStarted)
        {
            return false;
        }

        context.Response.StatusCode = problem.Status;
        if (problem.RetryAfterSeconds is { } retryAfter)
        {
            context.Response.Headers[HeaderNames.RetryAfter] = retryAfter.ToString(System.Globalization.CultureInfo.InvariantCulture);
        }

        var details = ToDetails(problem);
        var service = context.RequestServices?.GetService<IProblemDetailsService>();
        if (service is not null && await service.TryWriteAsync(new ProblemDetailsContext { HttpContext = context, ProblemDetails = details }))
        {
            return true;
        }

        context.Response.ContentType = ProblemContentType;
        await JsonSerializer.SerializeAsync(context.Response.Body, details, ProblemJson.Options, context.RequestAborted);
        return true;
    }

    /// <summary>The problem as <see cref="ProblemDetails"/> (type, title, status and the <c>code</c> extension).</summary>
    public static ProblemDetails ToDetails(SecurityProblem problem)
    {
        ArgumentNullException.ThrowIfNull(problem);
        var details = new ProblemDetails { Status = problem.Status, Title = problem.Title, Type = TypeFor(problem.Status) };
        if (problem.Code is not null)
        {
            details.Extensions[CodeMember] = problem.Code;
        }

        return details;
    }

    private static string TypeFor(int status) => status switch
    {
        StatusCodes.Status400BadRequest => TypeBase + "5.1",
        StatusCodes.Status401Unauthorized => TypeBase + "5.2",
        StatusCodes.Status403Forbidden => TypeBase + "5.4",
        StatusCodes.Status413PayloadTooLarge => TypeBase + "5.14",
        StatusCodes.Status429TooManyRequests => "https://tools.ietf.org/html/rfc6585#section-4",
        StatusCodes.Status503ServiceUnavailable => TypeBase + "6.4",
        _ => TypeBase + "6.1",
    };

    private static class ProblemJson
    {
        internal static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web);
    }
}

/// <summary>A <see cref="SecurityProblem"/> as an MVC action result and a minimal-API result.</summary>
public sealed class SecurityProblemResult(SecurityProblem problem) : IActionResult, IResult
{
    /// <summary>The problem.</summary>
    public SecurityProblem Problem { get; } = problem;

    /// <inheritdoc />
    public Task ExecuteResultAsync(ActionContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        return ExecuteAsync(context.HttpContext);
    }

    /// <inheritdoc />
    public Task ExecuteAsync(HttpContext httpContext) => SecurityProblemWriter.TryWriteAsync(httpContext, Problem);
}
