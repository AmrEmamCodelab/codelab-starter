using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using Starter.DevIdp.Authorize;
using Starter.DevIdp.Configuration;
using Starter.DevIdp.Discovery;
using Starter.DevIdp.EndSession;
using Starter.DevIdp.Grants;
using Starter.DevIdp.Hosting;
using Starter.DevIdp.Keys;
using Starter.DevIdp.Protocol;
using Starter.DevIdp.Token;
using Starter.DevIdp.UserInfo;

namespace Starter.DevIdp;

/// <summary>
/// The dev IdP composition: <see cref="Build"/> for the standalone process (Kestrel on <see cref="DefaultUrl"/>),
/// <see cref="AddDevIdp"/> + <see cref="MapDevIdp"/> for any other host, and <see cref="DevIdpInProcessServer"/> for
/// tests. Every path refuses a non-Development environment (<see cref="DevIdpEnvironmentGuard"/>).
/// </summary>
public static class DevIdpApp
{
    /// <summary>The default listen URL (and issuer).</summary>
    public const string DefaultUrl = DevIdpOptions.DefaultIssuer;

    /// <summary>
    /// Builds the standalone dev IdP from <paramref name="args"/> (ASP.NET Core command line, for example
    /// <c>--environment Development --urls http://localhost:5190</c>). Settings: the embedded appsettings.Development.json,
    /// then environment variables and the command line. Throws <see cref="DevIdpEnvironmentException"/> outside Development.
    /// </summary>
    public static WebApplication Build(string[] args)
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions
        {
            Args = args,
            ContentRootPath = AppContext.BaseDirectory,
            ApplicationName = typeof(DevIdpApp).Assembly.GetName().Name,
        });
        DevIdpEnvironmentGuard.EnsureDevelopment(builder.Environment);
        DevIdpEmbeddedSettings.AddAsDefaults(builder.Configuration);
        if (string.IsNullOrEmpty(builder.Configuration[WebHostDefaults.ServerUrlsKey]))
        {
            builder.WebHost.UseUrls(DefaultUrl);
        }

        builder.Services.AddDevIdp(builder.Configuration);
        var app = builder.Build();
        app.MapDevIdp();
        return app;
    }

    /// <summary>
    /// Registers the dev IdP services, bound to the <see cref="DevIdpOptions.SectionName"/> section and validated at
    /// start. A <see cref="TimeProvider"/> registered before this call is kept (tests use a fake clock).
    /// </summary>
    public static IServiceCollection AddDevIdp(this IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        services.AddOptions<DevIdpOptions>().Bind(configuration.GetSection(DevIdpOptions.SectionName)).ValidateOnStart();
        services.AddSingleton<IValidateOptions<DevIdpOptions>, DevIdpOptionsValidator>();
        services.TryAddSingleton(TimeProvider.System);
        services.AddSingleton<DevIdpSigningKey>();
        services.AddSingleton<ExpiringStore<AuthorizationGrant>>();
        services.AddSingleton<ExpiringStore<AccessGrant>>();
        services.AddSingleton<TokenIssuer>();
        services.AddSingleton<IdTokenHintValidator>();
        services.AddSingleton<AuthorizeEndpoint>();
        services.AddSingleton<TokenEndpoint>();
        services.AddSingleton<UserInfoEndpoint>();
        services.AddSingleton<EndSessionEndpoint>();
        return services;
    }

    /// <summary>Maps the dev IdP endpoints (<see cref="OidcRoutes"/>). Throws <see cref="DevIdpEnvironmentException"/> outside Development.</summary>
    public static IEndpointRouteBuilder MapDevIdp(this IEndpointRouteBuilder endpoints)
    {
        ArgumentNullException.ThrowIfNull(endpoints);
        DevIdpEnvironmentGuard.EnsureDevelopment(endpoints.ServiceProvider.GetRequiredService<IHostEnvironment>());
        endpoints.MapGet(OidcRoutes.Discovery, (IOptions<DevIdpOptions> options) => Results.Json(DiscoveryDocument.For(options.Value)));
        endpoints.MapGet(OidcRoutes.Jwks, (DevIdpSigningKey key) => Results.Json(new JwksDocument([key.ToJwk()])));
        endpoints.MapGet(OidcRoutes.Authorize, (HttpRequest request, AuthorizeEndpoint authorize) => authorize.Handle(OidcParameters.From(request.Query)));
        endpoints.MapPost(OidcRoutes.Authorize, (HttpRequest request, AuthorizeEndpoint authorize) => authorize.HandleFormAsync(request))
            .DisableAntiforgery();
        endpoints.MapPost(OidcRoutes.Token, (HttpContext context, TokenEndpoint token) => token.HandleAsync(context)).DisableAntiforgery();
        endpoints.MapMethods(OidcRoutes.UserInfo, [HttpMethods.Get, HttpMethods.Post], (HttpContext context, UserInfoEndpoint userInfo) => userInfo.Handle(context));
        endpoints.MapGet(OidcRoutes.EndSession, (HttpRequest request, EndSessionEndpoint endSession) => endSession.HandleAsync(OidcParameters.From(request.Query)));
        return endpoints;
    }
}
