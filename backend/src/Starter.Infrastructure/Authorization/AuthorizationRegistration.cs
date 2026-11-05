using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

namespace Starter.Infrastructure.Authorization;

/// <summary>The kernel authentication and authorization defaults.</summary>
public static class AuthorizationRegistration
{
    /// <summary>
    /// Adds authentication with <see cref="NoCredentialsAuthenticationHandler"/> as the default scheme while no module
    /// names one (the Identity module's bearer scheme replaces it), and authorization with a fallback policy that
    /// requires an authenticated user (<see cref="AuthenticatedOrUnroutedRequirement"/>), so an endpoint that forgot
    /// its attribute is closed, never open. Anonymous endpoints opt out with <c>[AllowAnonymous]</c> and are allowlisted.
    /// </summary>
    public static IServiceCollection AddStarterAuthorization(this IServiceCollection services)
    {
        services.AddAuthentication()
            .AddScheme<AuthenticationSchemeOptions, NoCredentialsAuthenticationHandler>(NoCredentialsAuthenticationHandler.SchemeName, configureOptions: null);
        services.PostConfigure<AuthenticationOptions>(UseNoCredentialsWhenUnset);
        services.AddAuthorizationBuilder()
            .SetFallbackPolicy(new AuthorizationPolicyBuilder().AddRequirements(new AuthenticatedOrUnroutedRequirement()).Build());
        return services;
    }

    /// <summary>Makes the no-credentials scheme the default when no module set a default scheme.</summary>
    public static void UseNoCredentialsWhenUnset(AuthenticationOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        if (options.DefaultScheme is null && options.DefaultAuthenticateScheme is null && options.DefaultChallengeScheme is null)
        {
            options.DefaultScheme = NoCredentialsAuthenticationHandler.SchemeName;
        }
    }
}

/// <summary>
/// The fallback requirement: the caller is authenticated, or the request matched no endpoint. A request that matches
/// nothing then reaches the 404 problem+json answer instead of a challenge; every real endpoint stays closed to
/// anonymous callers unless it says <c>[AllowAnonymous]</c>.
/// </summary>
public sealed class AuthenticatedOrUnroutedRequirement : AuthorizationHandler<AuthenticatedOrUnroutedRequirement>, IAuthorizationRequirement
{
    /// <inheritdoc />
    protected override Task HandleRequirementAsync(AuthorizationHandlerContext context, AuthenticatedOrUnroutedRequirement requirement)
    {
        ArgumentNullException.ThrowIfNull(context);
        if (IsSatisfied(context))
        {
            context.Succeed(requirement);
        }

        return Task.CompletedTask;
    }

    /// <summary>True when the user is authenticated or the resource is a request without an endpoint.</summary>
    public static bool IsSatisfied(AuthorizationHandlerContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        var authenticated = context.User.Identities.Any(identity => identity.IsAuthenticated);
        var unrouted = context.Resource is HttpContext http && http.GetEndpoint() is null;
        return authenticated || unrouted;
    }
}
