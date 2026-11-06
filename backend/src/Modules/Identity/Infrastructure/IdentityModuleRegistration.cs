using System.Reflection;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Starter.Application.Notifications;
using Starter.Application.Users;
using Starter.Application.Validation;
using Starter.Infrastructure.Modularity;
using Starter.Infrastructure.Persistence;
using Starter.Infrastructure.Startup;
using Starter.Modules.Identity.Application;
using Starter.Modules.Identity.Application.Accounts;
using Starter.Modules.Identity.Application.Activation;
using Starter.Modules.Identity.Application.Auditing;
using Starter.Modules.Identity.Infrastructure.Accounts;
using Starter.Modules.Identity.Infrastructure.Auditing;
using Starter.Modules.Identity.Infrastructure.Notifications;
using Starter.Modules.Identity.Infrastructure.Options;
using Starter.Modules.Identity.Infrastructure.Persistence;
using Starter.Modules.Identity.Infrastructure.Security;
using Starter.Modules.Identity.Infrastructure.Startup;
using IdentityPasswordHasherPort = Starter.Modules.Identity.Application.IPasswordHasher;

namespace Starter.Modules.Identity.Infrastructure;

/// <summary>The Identity module's registrations (called by <see cref="IdentityModule"/>).</summary>
public static class IdentityModuleRegistration
{
    /// <summary>
    /// Registers the options (validated on start), the DbContext in schema <c>identity</c>, the session issuer and
    /// its token adapters, the use cases and validators, <see cref="IUserDirectory"/>, <see cref="IAccountAdministration"/>,
    /// <see cref="INotificationRecipients"/>, the activation tickets, the auth event sink, the account seeding task and the JWT bearer scheme (the default
    /// authentication scheme) with its per-request <c>sid</c> revocation check.
    /// </summary>
    public static IServiceCollection AddIdentityModule(this IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        services.TryAddSingleton(TimeProvider.System);
        AddOptions(services, configuration);
        services.AddModuleDbContext<IdentityDbContext>(IdentityDbContext.SchemaName);
        services.AddScoped<IIdentityUnitOfWork>(provider => provider.GetRequiredService<IdentityDbContext>());
        services.AddScoped<IUserAccountRepository, UserAccountRepository>();
        services.AddScoped<IUserSessionRepository, UserSessionRepository>();
        AddSessions(services);
        AddUseCases(services);
        AddAccounts(services);
        services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme).AddJwtBearer();
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IConfigureOptions<JwtBearerOptions>, BearerAuthentication>());
        return services;
    }

    /// <summary>Maps nothing: the module's endpoints are the controllers in its <c>.Api</c> namespace.</summary>
    public static IEndpointRouteBuilder UseIdentityModule(this IEndpointRouteBuilder endpoints) => endpoints;

    private static void AddOptions(IServiceCollection services, IConfiguration configuration)
    {
        AddValidated<SessionOptions, SessionOptionsValidator>(services, configuration.GetSection(SessionOptions.SectionName));
        AddValidated<JwtOptions, JwtOptionsValidator>(services, configuration.GetSection(JwtOptions.SectionName));
        AddValidated<BreakGlassOptions, BreakGlassOptionsValidator>(services, configuration.GetSection(BreakGlassOptions.SectionName));
        services.AddOptions<DevUsersOptions>()
            .Configure(options => configuration.GetSection(DevUsersOptions.SectionName).Bind(options.Users))
            .ValidateOnStart();
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IValidateOptions<DevUsersOptions>, DevUsersOptionsValidator>());
        services.AddOptions<JwtOptions>().PostConfigure<IHostEnvironment>(DefaultIssuerAndAudience);
        AddValidated<LockoutOptions, LockoutOptionsValidator>(services, configuration.GetSection(LockoutOptions.SectionName));
        AddValidated<PasswordPolicyOptions, PasswordPolicyOptionsValidator>(services, configuration.GetSection(PasswordPolicyOptions.SectionName));
        AddValidated<ActivationOptions, ActivationOptionsValidator>(services, configuration.GetSection(ActivationOptions.SectionName));
        services.AddSingleton(provider => provider.GetRequiredService<IOptions<LockoutOptions>>().Value.ToPolicy());
        services.AddSingleton(provider => provider.GetRequiredService<IOptions<PasswordPolicyOptions>>().Value.ToPolicy());
    }

    private static void AddValidated<TOptions, TValidator>(IServiceCollection services, IConfigurationSection section)
        where TOptions : class
        where TValidator : class, IValidateOptions<TOptions>
    {
        services.AddOptions<TOptions>().Bind(section).ValidateOnStart();
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IValidateOptions<TOptions>, TValidator>());
    }

    private static void AddSessions(IServiceCollection services)
    {
        services.AddSingleton(provider => provider.GetRequiredService<IOptions<SessionOptions>>().Value.ToSettings());
        services.AddSingleton<SessionKeyIdSource>();
        services.AddSingleton<IAccessTokenIssuer, JwtAccessTokenIssuer>();
        services.AddSingleton<ISecureTokenGenerator, SecureTokenGenerator>();
        services.AddSingleton<IdentityPasswordHasherPort, IdentityPasswordHasher>();
        services.AddSingleton<ISessionRevocation, SessionRevocationAdapter>();
        services.AddSingleton<ISessionVersions, SessionVersionsAdapter>();
        services.AddSingleton<SessionTokens>();
        services.AddScoped<SessionIssuer>();
    }

    private static void AddUseCases(IServiceCollection services)
    {
        services.AddSingleton<IValidator<LoginCommand>, LoginCommandValidator>();
        services.AddSingleton<IValidator<RefreshCommand>, RefreshCommandValidator>();
        services.AddScoped<PasswordCheck>();
        services.AddScoped<LoginUseCase>();
        services.AddScoped<RefreshSessionUseCase>();
        services.AddScoped<LogoutUseCase>();
        services.AddScoped<SignInExternalAccount>();
        services.AddScoped<CurrentSessionQuery>();
        services.AddScoped<IUserDirectory, UserDirectory>();
        services.AddScoped<AccountSeeder>();
        services.AddScoped<IStartupTask, AccountSeedingTask>();
        services.AddScoped<IAuthEventSink, EfAuthEventSink>();
    }

    private static void AddAccounts(IServiceCollection services)
    {
        services.AddSingleton<AccountInputValidator>();
        services.AddSingleton<IActivationTickets, SealedActivationTickets>();
        services.AddSingleton<IValidator<ActivateAccountCommand>, ActivateAccountCommandValidator>();
        services.AddScoped<AccountWrites>();
        services.AddScoped<ActivationIssuer>();
        services.AddScoped<ActivateAccountUseCase>();
        services.AddScoped<IAccountAdministration, AccountAdministration>();
        services.AddScoped<INotificationRecipients, NotificationRecipients>();
    }

    private static void DefaultIssuerAndAudience(JwtOptions options, IHostEnvironment environment)
    {
        if (!string.IsNullOrWhiteSpace(options.Issuer) && !string.IsNullOrWhiteSpace(options.Audience))
        {
            return;
        }

        var serviceName = ModuleNaming.ServiceNameOf(Assembly.Load(new AssemblyName(environment.ApplicationName)));
        options.Issuer = string.IsNullOrWhiteSpace(options.Issuer) ? serviceName : options.Issuer;
        options.Audience = string.IsNullOrWhiteSpace(options.Audience) ? serviceName : options.Audience;
    }
}
