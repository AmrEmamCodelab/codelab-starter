using Codelab.Attachments.Api.Authentication;
using Codelab.Attachments.Api.Endpoints;
using Codelab.Observability;
using Codelab.Security.Cors;
using Codelab.Security.Tickets;
using Codelab.Security.Uploads;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;

namespace Codelab.Attachments.Api.Hosting;

/// <summary>The host wiring Program.cs calls: services (<see cref="AddAttachmentsHost"/>) and the pipeline (<see cref="UseAttachmentsPipeline"/>).</summary>
public static class AttachmentsHostExtensions
{
    /// <summary>The name of the storage readiness check.</summary>
    public const string StorageCheckName = "attachment-storage";

    /// <summary>
    /// Registers the two authentication schemes (the S2S service assertion and the one-time upload ticket) and their
    /// policies (the S2S one is the fallback, so an endpoint mapped without a policy is S2S only), the upload CORS
    /// policy, the start-up checks this host adds to Codelab.Security, and the storage readiness check. Call it after
    /// <c>AddCodelabSecurity</c> and <c>AddAttachmentsCore</c>.
    /// </summary>
    public static IServiceCollection AddAttachmentsHost(this IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);
        services.AddAttachmentsAuthentication(configuration);
        services.AddUploadCors(configuration);
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IValidateOptions<SealedTicketOptions>, RequiredSecurityFeaturesValidator>());
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IValidateOptions<UploadSafetyOptions>, RequiredSecurityFeaturesValidator>());
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IValidateOptions<CodelabCorsOptions>, RequiredSecurityFeaturesValidator>());
        services.AddHealthChecks().AddCheck<StorageReadinessCheck>(StorageCheckName, tags: [HealthEndpoints.ReadyTag]);
        return services;
    }

    /// <summary>Routing, then CORS (the upload endpoint's policy only), authentication and authorization.</summary>
    public static IApplicationBuilder UseAttachmentsPipeline(this IApplicationBuilder app)
    {
        ArgumentNullException.ThrowIfNull(app);
        app.UseRouting();
        app.UseCors();
        app.UseAuthentication();
        app.UseAuthorization();
        return app;
    }

    /// <summary>Maps the upload and file endpoints (<see cref="AttachmentsEndpoints"/>).</summary>
    public static IEndpointRouteBuilder MapAttachmentsEndpoints(this IEndpointRouteBuilder endpoints)
    {
        ArgumentNullException.ThrowIfNull(endpoints);
        AttachmentsEndpoints.Map(endpoints);
        return endpoints;
    }
}
