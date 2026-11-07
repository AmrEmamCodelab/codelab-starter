using Codelab.OpsConsole.Api.Hosting;
using Microsoft.AspNetCore.Authentication.Negotiate;
using Microsoft.AspNetCore.Authorization;

namespace Codelab.OpsConsole.Api.Authentication;

/// <summary>
/// The console's one operator credential: Windows sign-in (Negotiate: Kerberos, NTLM fallback; under IIS it forwards to
/// the server's Windows authentication) plus membership of the operator group. In Development with
/// <c>Codelab:OpsConsole:DevOperator:Enabled</c> the dev operator replaces Windows sign-in. There is no JWT, API key or
/// cookie. <see cref="OperatorPolicy"/> is the default and the fallback policy, so an endpoint mapped without a policy
/// is operators only; the policy names no scheme, so it always uses the default scheme registered here.
/// </summary>
public static class OperatorAuthentication
{
    /// <summary>The policy every console endpoint requires: authenticated and a member of the operator group.</summary>
    public const string OperatorPolicy = "ops-operator";

    internal static IServiceCollection AddOperatorAuthentication(this IServiceCollection services, OpsConsoleOptions options, IHostEnvironment environment)
    {
        if (UsesDevOperator(options, environment))
        {
            services.AddAuthentication(DevOperatorAuthenticationHandler.SchemeName)
                .AddScheme<DevOperatorAuthenticationOptions, DevOperatorAuthenticationHandler>(DevOperatorAuthenticationHandler.SchemeName, configureOptions: null);
        }
        else
        {
            services.AddAuthentication(NegotiateDefaults.AuthenticationScheme).AddNegotiate();
        }

        services.AddSingleton<IAuthorizationHandler, OperatorGroupHandler>();
        var operatorOnly = new AuthorizationPolicyBuilder()
            .RequireAuthenticatedUser()
            .AddRequirements(new OperatorGroupRequirement())
            .Build();
        services.AddAuthorizationBuilder()
            .AddPolicy(OperatorPolicy, operatorOnly)
            .SetDefaultPolicy(operatorOnly)
            .SetFallbackPolicy(operatorOnly);
        return services;
    }

    /// <summary>
    /// True when the dev operator replaces Windows sign-in: enabled in Development. Enabled anywhere else throws, so a
    /// copied Development setting can never open a deployed console.
    /// </summary>
    /// <exception cref="InvalidOperationException">The dev operator is enabled outside Development.</exception>
    public static bool UsesDevOperator(OpsConsoleOptions options, IHostEnvironment environment)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(environment);
        if (!options.DevOperator.Enabled)
        {
            return false;
        }

        return environment.IsDevelopment()
            ? true
            : throw new InvalidOperationException($"{OpsConsoleOptions.SectionName}:DevOperator:Enabled is allowed only in Development.");
    }
}
