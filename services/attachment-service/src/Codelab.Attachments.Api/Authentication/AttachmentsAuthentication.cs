using Codelab.Security.ServiceAuth;
using Codelab.Security.Sessions;
using Microsoft.AspNetCore.Authorization;

namespace Codelab.Attachments.Api.Authentication;

/// <summary>
/// The service's two credentials and nothing else. There is no default scheme, no JWT bearer and no API key: an
/// <c>Authorization</c> header or an API-key header is never read, so it authenticates nothing and the request is
/// challenged with 401. Each policy names its one scheme, so a ticket never opens on a file endpoint and a service
/// assertion never authorizes an upload.
/// </summary>
public static class AttachmentsAuthentication
{
    /// <summary>S2S only (the fallback policy): a valid ES256 service assertion from a trusted service.</summary>
    public const string ServiceCallerPolicy = "attachments-service-caller";

    /// <summary>Browser upload only: an opened one-time upload ticket.</summary>
    public const string UploadTicketPolicy = "attachments-upload-ticket";

    internal static IServiceCollection AddAttachmentsAuthentication(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddAuthentication()
            .AddCodelabServiceAssertion(configuration)
            .AddScheme<UploadTicketAuthenticationOptions, UploadTicketAuthenticationHandler>(UploadTicketAuthenticationHandler.SchemeName, configureOptions: null);

        var serviceCaller = new AuthorizationPolicyBuilder(ServiceAssertionAuthenticationHandler.SchemeName)
            .RequireAuthenticatedUser()
            .RequireClaim(SecurityClaimTypes.Service)
            .Build();
        var ticketHolder = new AuthorizationPolicyBuilder(UploadTicketAuthenticationHandler.SchemeName)
            .RequireAuthenticatedUser()
            .RequireClaim(UploadTickets.ScopeClaimType)
            .Build();
        services.AddAuthorizationBuilder()
            .AddPolicy(ServiceCallerPolicy, serviceCaller)
            .AddPolicy(UploadTicketPolicy, ticketHolder)
            .SetDefaultPolicy(serviceCaller)
            .SetFallbackPolicy(serviceCaller);
        return services;
    }
}
