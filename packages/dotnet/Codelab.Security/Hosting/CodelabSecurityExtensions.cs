using Codelab.Security.PublicIds;
using Codelab.Security.Sessions;
using Codelab.Security.Signing;
using Codelab.Security.Tickets;
using Codelab.Security.Uploads;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace Codelab.Security;

/// <summary>The one registration and the one pipeline call of Codelab.Security.</summary>
public static class CodelabSecurityExtensions
{
    private const long SelfTestId = 1;

    /// <summary>Registers every Codelab.Security service from <c>Codelab:Security:*</c> with start-up validation.</summary>
    public static IHostApplicationBuilder AddCodelabSecurity(this IHostApplicationBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        builder.Services.AddCodelabSecurity(builder.Configuration);
        return builder;
    }

    /// <summary>
    /// Registers every Codelab.Security service: protocol, key-ring guards, PublicIds, request/response signing, the
    /// replay guard, sessions, headers, CORS, rate limiting, tickets and upload safety. Every options type is bound from
    /// its section and validated on start (ValidateOnStart), so a misconfigured or published key fails the boot.
    /// Service-to-service assertions are added separately (AddCodelabServiceAssertion on the authentication or
    /// HTTP client builder).
    /// </summary>
    public static IServiceCollection AddCodelabSecurity(this IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);
        Hosting.SecurityRegistration.AddCore(services, configuration);
        Hosting.SecurityRegistration.AddPublicIds(services, configuration);
        Hosting.SecurityRegistration.AddSigning(services, configuration);
        Hosting.SecurityRegistration.AddReplay(services, configuration);
        Hosting.SecurityRegistration.AddSessions(services, configuration);
        Hosting.SecurityRegistration.AddEdge(services, configuration);
        AddTicketsAndUploads(services, configuration);
        return services;
    }

    /// <summary>
    /// Adds the security pipeline. Call it after authentication (WebApplication adds UseAuthentication automatically
    /// when authentication is registered) and before the endpoints: the rate limiter, session revocation checks,
    /// request signature verification. The security headers and response signing need no call: AddCodelabSecurity puts
    /// them first in the pipeline (startup filters), so every response carries the headers and the response signature
    /// covers the body the host's error handling writes (status-code pages, exception handler). Also installs the PublicId codec and
    /// round-trips a self-test identifier, so a broken PublicId ring fails here rather than on the first request.
    /// Call it after UseRouting: when the endpoint's policy names authentication schemes, they are authenticated first,
    /// so the session and signature checks see the principal authorization will use (not only the default scheme's).
    /// </summary>
    public static IApplicationBuilder UseCodelabSecurity(this IApplicationBuilder app)
    {
        ArgumentNullException.ThrowIfNull(app);
        InstallPublicIdCodec(app.ApplicationServices);
        app.Use(RequireAuthenticationFirst);
        app.Use(Hosting.EndpointPrincipal.ResolveAsync);
        app.UseRateLimiter();
        app.UseMiddleware<SessionValidationMiddleware>();
        app.UseMiddleware<RequestSignatureMiddleware>();
        return app;
    }

    private static void AddTicketsAndUploads(IServiceCollection services, IConfiguration configuration)
    {
        Hosting.SecurityRegistration.AddValidatedOptions<SealedTicketOptions, Hosting.SealedTicketOptionsValidator>(services, configuration, SealedTicketOptions.SectionName);
        services.TryAddSingleton<ISealedTicketService>(provider => new SealedTicketService(
            EnabledOrThrow(provider.GetRequiredService<IOptions<SealedTicketOptions>>().Value, options => options.Enabled, SealedTicketOptions.SectionName),
            provider.GetRequiredService<Replay.IReplayGuard>(),
            provider.GetRequiredService<TimeProvider>(),
            provider.GetRequiredService<SecurityProtocol>()));
        Hosting.SecurityRegistration.AddValidatedOptions<UploadSafetyOptions, Hosting.UploadSafetyOptionsValidator>(services, configuration, UploadSafetyOptions.SectionName);
        services.TryAddSingleton(provider => new FileSignatureSniffer(provider.GetRequiredService<IOptions<UploadSafetyOptions>>().Value.AllowedExtensions));
        services.TryAddSingleton<IUploadReceiptSigner>(provider => new UploadReceiptSigner(
            EnabledOrThrow(provider.GetRequiredService<IOptions<UploadSafetyOptions>>().Value, options => options.ReceiptsEnabled, UploadSafetyOptions.SectionName),
            provider.GetRequiredService<SecurityProtocol>()));
    }

    private static T EnabledOrThrow<T>(T options, Func<T, bool> isEnabled, string section) =>
        isEnabled(options) ? options : throw new InvalidOperationException($"{section} is disabled; enable it in configuration before resolving its service.");

    private static void InstallPublicIdCodec(IServiceProvider services)
    {
        var codec = services.GetRequiredService<IPublicIdCodec>();
        var installation = PublicIdCodecAccessor.Install(codec);
        services.GetService<IHostApplicationLifetime>()?.ApplicationStopped.Register(installation.Dispose);
        var probe = PublicId<SelfTestDomain>.From(SelfTestId).ToToken();
        if (!PublicId<SelfTestDomain>.TryParse(probe, out var readBack) || readBack.Value != SelfTestId)
        {
            throw new InvalidOperationException($"The PublicId self-test failed: {PublicIdKeyringOptions.SectionName} is configured but not usable.");
        }
    }

    private static async Task RequireAuthenticationFirst(HttpContext context, RequestDelegate next)
    {
        var signing = context.RequestServices.GetRequiredService<IOptionsMonitor<RequestSigningOptions>>().CurrentValue;
        if (signing.Mode is not SecurityEnforcementMode.Off && context.Features.Get<IAuthenticationFeature>() is null)
        {
            throw new InvalidOperationException("UseCodelabSecurity() ran before authentication while request signing is on; every request would pass unverified. Call it after UseAuthentication().");
        }

        await next(context);
    }

    private sealed class SelfTestDomain : IIdDomain
    {
        public static string Domain => "codelab-selftest";
    }
}
