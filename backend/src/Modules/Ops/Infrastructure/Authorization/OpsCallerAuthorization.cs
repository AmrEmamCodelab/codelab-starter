using System.Text.Encodings.Web;
using Codelab.Security.ServiceAuth;
using Codelab.Security.Sessions;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Starter.Modules.Ops.Api;
using Starter.Modules.Ops.Infrastructure.Options;

namespace Starter.Modules.Ops.Infrastructure.Authorization;

/// <summary>The caller is the trusted Ops Console API, authenticated by its service assertion.</summary>
public sealed class OpsTrustedCallerRequirement : IAuthorizationRequirement;

/// <summary>
/// Succeeds when every authenticated identity is a service identity (<see cref="SessionClaims.IsService"/>) and the
/// service is the one whose key id is <c>Codelab:Ops:TrustedCallerKeyId</c> in
/// <c>Codelab:Security:ServiceAssertions:Server:TrustedServices</c>. The assertion validator already bound the issuer
/// to its key, so the service name identifies the key.
/// </summary>
public sealed class OpsTrustedCallerHandler(IOptionsMonitor<OpsOptions> ops, IOptionsMonitor<ServiceAssertionServerOptions> server)
    : AuthorizationHandler<OpsTrustedCallerRequirement>
{
    /// <summary>The service name trusted for the Ops surface, or null when none is configured.</summary>
    public static string? TrustedServiceName(OpsOptions ops, ServiceAssertionServerOptions server)
    {
        ArgumentNullException.ThrowIfNull(ops);
        ArgumentNullException.ThrowIfNull(server);
        return ops.IsOpen
            ? server.TrustedServices.FirstOrDefault(service => string.Equals(service.KeyId, ops.TrustedCallerKeyId, StringComparison.Ordinal))?.ServiceName
            : null;
    }

    /// <inheritdoc />
    protected override Task HandleRequirementAsync(AuthorizationHandlerContext context, OpsTrustedCallerRequirement requirement)
    {
        ArgumentNullException.ThrowIfNull(context);
        var trusted = TrustedServiceName(ops.CurrentValue, server.CurrentValue);
        var caller = context.User.FindFirst(SecurityClaimTypes.Service)?.Value;
        if (trusted is not null && SessionClaims.IsService(context.User) && string.Equals(caller, trusted, StringComparison.Ordinal))
        {
            context.Succeed(requirement);
        }

        return Task.CompletedTask;
    }
}

/// <summary>
/// Stands in for the service-assertion scheme while <c>Codelab:Ops:TrustedCallerKeyId</c> is empty: it authenticates
/// nothing, so every Ops call is challenged with 401, and the host starts without server assertion settings (test
/// hosts, DocsGen, a deployment that does not use the Ops Console).
/// </summary>
public sealed class ClosedServiceAssertionHandler(
    IOptionsMonitor<ServiceAssertionAuthenticationOptions> options,
    ILoggerFactory loggerFactory,
    UrlEncoder encoder)
    : AuthenticationHandler<ServiceAssertionAuthenticationOptions>(options, loggerFactory, encoder)
{
    /// <inheritdoc />
    protected override Task<AuthenticateResult> HandleAuthenticateAsync() => Task.FromResult(AuthenticateResult.NoResult());
}

/// <summary>The Ops surface's authentication and authorization registrations.</summary>
public static class OpsCallerAuthorization
{
    /// <summary>
    /// Registers <see cref="OpsServiceCallerAttribute.PolicyName"/> (service-assertion scheme only, authenticated,
    /// <see cref="OpsTrustedCallerRequirement"/>) and the scheme: the real Codelab.Security server scheme when a trusted
    /// caller is configured (unless another module already added it), otherwise <see cref="ClosedServiceAssertionHandler"/>.
    /// </summary>
    public static IServiceCollection AddOpsCallerAuthorization(this IServiceCollection services, IConfiguration configuration, bool isOpen)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        var schemeRegistered = services.Any(descriptor => descriptor.ServiceType == typeof(ServiceAssertionValidator));
        if (!schemeRegistered)
        {
            var authentication = services.AddAuthentication();
            if (isOpen)
            {
                authentication.AddCodelabServiceAssertion(configuration);
            }
            else
            {
                authentication.AddScheme<ServiceAssertionAuthenticationOptions, ClosedServiceAssertionHandler>(
                    ServiceAssertionAuthenticationHandler.SchemeName, configureOptions: null);
            }
        }

        services.AddOptions<ServiceAssertionServerOptions>();
        services.AddSingleton<IAuthorizationHandler, OpsTrustedCallerHandler>();
        services.AddSingleton<IValidateOptions<OpsOptions>, OpsTrustedCallerValidator>();
        services.AddAuthorizationBuilder().AddPolicy(OpsServiceCallerAttribute.PolicyName, policy => policy
            .AddAuthenticationSchemes(ServiceAssertionAuthenticationHandler.SchemeName)
            .RequireAuthenticatedUser()
            .AddRequirements(new OpsTrustedCallerRequirement()));
        return services;
    }
}

/// <summary>
/// Stops the host at start when <c>Codelab:Ops:TrustedCallerKeyId</c> is set but names no entry of
/// <c>Codelab:Security:ServiceAssertions:Server:TrustedServices</c>.
/// </summary>
public sealed class OpsTrustedCallerValidator(IOptions<ServiceAssertionServerOptions> server) : IValidateOptions<OpsOptions>
{
    /// <inheritdoc />
    public ValidateOptionsResult Validate(string? name, OpsOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        if (!options.IsOpen || OpsTrustedCallerHandler.TrustedServiceName(options, server.Value) is not null)
        {
            return ValidateOptionsResult.Success;
        }

        return ValidateOptionsResult.Fail(
            $"{OpsOptions.SectionName}:{nameof(OpsOptions.TrustedCallerKeyId)} names no entry of {ServiceAssertionServerOptions.SectionName}:{nameof(ServiceAssertionServerOptions.TrustedServices)}.");
    }
}
