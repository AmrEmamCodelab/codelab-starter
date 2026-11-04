using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Authorization.Policy;

namespace Codelab.Security.Hosting;

/// <summary>
/// Puts on <see cref="HttpContext.User"/> the principal authorization will use, before the session and signature
/// checks read it. UseAuthentication authenticates only the default scheme; an endpoint whose policy names its own
/// schemes (<c>[Authorize(AuthenticationSchemes = ...)]</c>, a policy with <c>AddAuthenticationSchemes</c>) is
/// authenticated by the authorization middleware later, after UseCodelabSecurity. Without this step a host with no
/// default scheme, or a default that is not the user scheme, would skip signing and <c>sid</c> revocation for those
/// endpoints. The same combination and the same <see cref="IPolicyEvaluator"/> as AuthorizationMiddleware are used,
/// so authentication handlers (which cache their result per request) are not run with a different outcome.
/// </summary>
internal static class EndpointPrincipal
{
    /// <summary>The middleware: authenticates the endpoint policy's schemes, when it names any, then continues.</summary>
    public static async Task ResolveAsync(HttpContext context, RequestDelegate next)
    {
        var endpoint = context.GetEndpoint();
        if (endpoint is not null)
        {
            await AuthenticatePolicySchemesAsync(context, endpoint);
        }

        await next(context);
    }

    private static async Task AuthenticatePolicySchemesAsync(HttpContext context, Endpoint endpoint)
    {
        var services = context.RequestServices;
        var policyProvider = services.GetService<IAuthorizationPolicyProvider>();
        var evaluator = services.GetService<IPolicyEvaluator>();
        if (policyProvider is null || evaluator is null)
        {
            return;
        }

        var policy = await AuthorizationPolicy.CombineAsync(
            policyProvider,
            endpoint.Metadata.GetOrderedMetadata<IAuthorizeData>(),
            endpoint.Metadata.GetOrderedMetadata<AuthorizationPolicy>());
        if (policy is { AuthenticationSchemes.Count: > 0 })
        {
            // Sets context.User to the merged principal of those schemes (anonymous when none succeeds), exactly as
            // AuthorizationMiddleware will.
            await evaluator.AuthenticateAsync(policy, context);
        }
    }
}
