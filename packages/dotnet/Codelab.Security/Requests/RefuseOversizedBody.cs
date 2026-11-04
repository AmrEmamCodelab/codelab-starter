using Codelab.Security.Problems;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Mvc.Filters;

namespace Codelab.Security.Requests;

/// <summary>
/// The request body ceiling: a request that declares a <c>Content-Length</c> above the body-size limit in force for
/// the endpoint (<c>[RequestSizeLimit]</c> or the server default) is refused 413 <c>payload_too_large</c> before
/// model binding reads a byte, instead of failing mid-read as a 500. A chunked body has no declared length to compare;
/// the server's own limit still stops it while reading.
/// </summary>
public static class RequestBodyCeiling
{
    /// <summary>The refusal written for an oversized body.</summary>
    public static readonly SecurityProblem Refusal =
        new(StatusCodes.Status413PayloadTooLarge, "The request body is larger than this endpoint accepts.", SecurityProblemCodes.PayloadTooLarge);

    /// <summary>True when the declared body length exceeds the limit in force.</summary>
    public static bool IsDeclaredOverLimit(HttpContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        var declared = context.Request.ContentLength;
        var limit = context.Features.Get<IHttpMaxRequestBodySizeFeature>()?.MaxRequestBodySize;
        return declared is not null && limit is not null && declared > limit;
    }

    /// <summary>Adds the ceiling to a minimal-API endpoint (or group).</summary>
    public static TBuilder RefuseOversizedBody<TBuilder>(this TBuilder builder)
        where TBuilder : IEndpointConventionBuilder =>
        builder.AddEndpointFilter(static async (context, next) =>
            IsDeclaredOverLimit(context.HttpContext) ? new SecurityProblemResult(Refusal) : await next(context));
}

/// <summary>The MVC form of <see cref="RequestBodyCeiling"/> (a resource filter, so it runs before model binding).</summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, AllowMultiple = false, Inherited = true)]
public sealed class RefuseOversizedBodyAttribute : Attribute, IResourceFilter
{
    /// <inheritdoc />
    public void OnResourceExecuting(ResourceExecutingContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        if (RequestBodyCeiling.IsDeclaredOverLimit(context.HttpContext))
        {
            context.Result = new SecurityProblemResult(RequestBodyCeiling.Refusal);
        }
    }

    /// <inheritdoc />
    public void OnResourceExecuted(ResourceExecutedContext context)
    {
        // Nothing to do after the action: the ceiling only gates the start of the request.
    }
}
