using Codelab.Observability;
using Codelab.Security.Sessions;
using Microsoft.AspNetCore.Mvc.Filters;
using Starter.Infrastructure.Results;
using Starter.Modules.Ops.Application.Access;
using Starter.Modules.Ops.Domain;

namespace Starter.Modules.Ops.Api;

/// <summary>Names the Ops endpoint an action is, for <see cref="OperatorAccessFilter"/>.</summary>
/// <param name="endpoint">The endpoint.</param>
[AttributeUsage(AttributeTargets.Method, AllowMultiple = false)]
public sealed class OpsEndpointAttribute(OpsEndpoint endpoint) : Attribute
{
    /// <summary>The endpoint.</summary>
    public OpsEndpoint Endpoint { get; } = endpoint;
}

/// <summary>
/// Runs after authorization and before every Ops action: hands the call (operator header, calling service, endpoint,
/// reference code, correlation id) to <see cref="RecordOperatorAccess"/>, and answers its refusal (400
/// <c>OPS_OPERATOR_REQUIRED</c>) instead of running the action.
/// </summary>
public sealed class OperatorAccessFilter(RecordOperatorAccess recordOperatorAccess) : IAsyncActionFilter
{
    /// <inheritdoc />
    public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(next);
        var http = context.HttpContext;
        var endpoint = context.ActionDescriptor.EndpointMetadata.OfType<OpsEndpointAttribute>().Single().Endpoint;
        var call = new OperatorCall(
            Operator: http.Request.Headers[OpsHeaders.Operator].ToString(),
            CallerService: http.User.FindFirst(SecurityClaimTypes.Service)?.Value ?? string.Empty,
            Endpoint: endpoint,
            Subject: context.RouteData.Values.GetValueOrDefault(OpsRoutes.CodeParameter) as string,
            CorrelationId: CorrelationId.For(http));
        var outcome = await recordOperatorAccess.ExecuteAsync(call, http.RequestAborted);
        if (!outcome.IsSuccess)
        {
            context.Result = outcome.Refusal.ToActionResult();
            return;
        }

        await next();
    }
}
