using System.Security.Claims;
using System.Text.Encodings.Web;
using Codelab.Security.Sessions;
using Codelab.Security.Signing;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;

namespace Codelab.Security.ServiceAuth;

/// <summary>Options of the service-assertion authentication scheme (the settings live in <see cref="ServiceAssertionServerOptions"/>).</summary>
public sealed class ServiceAssertionAuthenticationOptions : AuthenticationSchemeOptions;

/// <summary>
/// The server side of the one service-to-service scheme: reads <c>X-{Prefix}-Service-Assertion</c>, hashes the request
/// body (buffered and rewound), validates the assertion and signs in a service principal carrying
/// <see cref="SecurityClaimTypes.Service"/>. No header: no result (other schemes may still authenticate the request).
/// Any refusal fails authentication, which challenges with 401.
/// </summary>
public sealed class ServiceAssertionAuthenticationHandler(
    IOptionsMonitor<ServiceAssertionAuthenticationOptions> options,
    ILoggerFactory loggerFactory,
    UrlEncoder encoder,
    ServiceAssertionValidator validator,
    IOptionsMonitor<ServiceAssertionServerOptions> serverOptions)
    : AuthenticationHandler<ServiceAssertionAuthenticationOptions>(options, loggerFactory, encoder)
{
    /// <summary>The scheme name.</summary>
    public const string SchemeName = "CodelabServiceAssertion";

    /// <inheritdoc />
    protected override async Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        var protocol = Context.RequestServices.GetRequiredService<SecurityProtocol>();
        var token = Request.Headers[protocol.ServiceAssertionHeaderName].ToString();
        if (token.Length == 0)
        {
            return AuthenticateResult.NoResult();
        }

        var bodyHash = await SignedRequestBody.TryHashAsync(Request, serverOptions.CurrentValue.MaxBodyBytes, enforcing: true, Context.RequestAborted);
        var target = ServiceAssertionToken.Target(Request.PathBase.Add(Request.Path).ToUriComponent(), Request.QueryString.Value ?? string.Empty);
        var result = bodyHash is null
            ? ServiceAssertionResult.Refused(ServiceAssertionOutcome.BindingMismatch)
            : await validator.ValidateAsync(new ServiceAssertionRequest(token, Request.Method, target, bodyHash), Context.RequestAborted);
        if (result.Outcome is not ServiceAssertionOutcome.Valid)
        {
            SecurityLog.ServiceAssertionRejected(Logger, Request.Method, Request.Path, result.Outcome.ToString());
            return AuthenticateResult.Fail(result.Outcome.ToString());
        }

        Claim[] claims = [new(ClaimTypes.NameIdentifier, result.ServiceName!), new(SecurityClaimTypes.Service, result.ServiceName!)];
        var principal = new ClaimsPrincipal(new ClaimsIdentity(claims, Scheme.Name));
        return AuthenticateResult.Success(new AuthenticationTicket(principal, Scheme.Name));
    }
}

/// <summary>Registered with the server scheme, so start-up checks know the replay guard serves service assertions.</summary>
internal sealed class ServiceAssertionServerMarker;

/// <summary>Registration of the service-to-service scheme (both sides).</summary>
public static class ServiceAssertionExtensions
{
    /// <summary>
    /// Adds the server scheme (<see cref="ServiceAssertionAuthenticationHandler.SchemeName"/>), binding
    /// <see cref="ServiceAssertionServerOptions"/> from configuration and validating it at start. Requires
    /// AddCodelabSecurity (protocol, replay guard, clock).
    /// </summary>
    public static AuthenticationBuilder AddCodelabServiceAssertion(this AuthenticationBuilder builder, Microsoft.Extensions.Configuration.IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(configuration);
        builder.Services.AddOptions<ServiceAssertionServerOptions>().Bind(configuration.GetSection(ServiceAssertionServerOptions.SectionName)).ValidateOnStart();
        builder.Services.AddSingleton<IValidateOptions<ServiceAssertionServerOptions>, ServiceAssertionServerOptionsValidator>();
        builder.Services.TryAddSingleton<ServiceAssertionServerMarker>();
        builder.Services.AddSingleton(services => new ServiceAssertionValidator(
            services.GetRequiredService<IOptions<ServiceAssertionServerOptions>>().Value,
            services.GetRequiredService<Replay.IReplayGuard>(),
            services.GetRequiredService<TimeProvider>(),
            services.GetRequiredService<SecurityProtocol>()));
        return builder.AddScheme<ServiceAssertionAuthenticationOptions, ServiceAssertionAuthenticationHandler>(ServiceAssertionAuthenticationHandler.SchemeName, configureOptions: null);
    }

    /// <summary>
    /// Adds the client handler to an HTTP client: every request carries a fresh assertion. Binds
    /// <see cref="ServiceAssertionClientOptions"/> from configuration and validates it at start.
    /// </summary>
    public static IHttpClientBuilder AddCodelabServiceAssertion(this IHttpClientBuilder builder, Microsoft.Extensions.Configuration.IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(configuration);
        builder.Services.AddOptions<ServiceAssertionClientOptions>().Bind(configuration.GetSection(ServiceAssertionClientOptions.SectionName)).ValidateOnStart();
        builder.Services.AddSingleton<IValidateOptions<ServiceAssertionClientOptions>, ServiceAssertionClientOptionsValidator>();
        builder.Services.AddSingleton(services => new ServiceAssertionIssuer(
            services.GetRequiredService<IOptions<ServiceAssertionClientOptions>>().Value,
            services.GetRequiredService<TimeProvider>(),
            services.GetRequiredService<SecurityProtocol>()));
        builder.Services.AddTransient<ServiceAssertionHandler>();
        return builder.AddHttpMessageHandler<ServiceAssertionHandler>();
    }
}
