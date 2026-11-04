using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;

namespace Codelab.Errors;

/// <summary>
/// Fills in the envelope for any 4xx/5xx the pipeline answered with an EMPTY body: a 401 from authentication, a 403
/// from an authorization policy, a routing 404, a 405, a 415, a minimal-API 400. Registered by
/// <c>UseCodelabErrors</c> through <c>UseStatusCodePages</c>. It stays cheap and never touches the request body.
/// </summary>
public static class StatusCodePagesWriter
{
    /// <summary>Writes the envelope for the response's status unless something already wrote a body.</summary>
    public static Task WriteAsync(StatusCodeContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        var httpContext = context.HttpContext;
        if (httpContext.Response.HasStarted || httpContext.Response.ContentLength is > 0)
        {
            return Task.CompletedTask;
        }

        var status = httpContext.Response.StatusCode;
        var description = FrameworkStatusCodes.Describe(status);
        var problem = new ProblemDetails
        {
            Status = status,
            Title = ErrorEnvelope.Title,
            Detail = description.Detail,
            Extensions = { [ErrorEnvelope.CodeMember] = description.Code },
        };
        return ProblemResponseWriter.WriteAsync(httpContext, problem);
    }
}
