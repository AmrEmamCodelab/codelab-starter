using Codelab.Errors;
using Codelab.Security.ServiceAuth;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Starter.Application.Attachments;
using Starter.Application.Authorization;
using Starter.Application.Validation;
using Starter.Infrastructure.Messaging;
using Starter.Infrastructure.Persistence;
using Starter.Infrastructure.Startup;
using Starter.Modules.Attachments.Application;
using Starter.Modules.Attachments.Infrastructure.Options;
using Starter.Modules.Attachments.Infrastructure.Persistence;
using Starter.Modules.Attachments.Infrastructure.Security;
using Starter.Modules.Attachments.Infrastructure.Startup;
using Starter.Modules.Attachments.Infrastructure.Storage;

namespace Starter.Modules.Attachments.Infrastructure;

/// <summary>The Attachments module's registrations (called by <see cref="AttachmentsModule"/>).</summary>
public static class AttachmentsModuleRegistration
{
    /// <summary>
    /// Registers the permission catalog source, the <c>Attachments</c> options (validated on start), the DbContext in
    /// schema <c>attachments</c> and its store, the receipt verifier, the upload-ticket ring
    /// (<c>Codelab:Security:UploadTickets</c>, validated on start, separate from the API's own ticket ring) and the
    /// ticket issuer over it, the attachment service
    /// client (typed <see cref="HttpClient"/> with the ES256 service assertion, <c>Codelab:Security:ServiceAssertions:Client</c>,
    /// validated on start), the outbox cleanup and its handler, the 503 classifier of a service outage, the start-up
    /// catalog check, the record access gate (the Development demo record type only in Development), the use cases,
    /// the validators and the kernel <see cref="IRequiredAttachments"/> interlock.
    /// </summary>
    public static IServiceCollection AddAttachmentsModule(this IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IPermissionCatalogSource, AttachmentsPermissionCatalogSource>());
        services.TryAddSingleton(TimeProvider.System);
        AddOptions(services, configuration);
        services.AddModuleDbContext<AttachmentsDbContext>(AttachmentsDbContext.SchemaName);
        services.AddScoped<IAttachmentStore, EfAttachmentStore>();
        AddSecurity(services);
        AddStorage(services, configuration);
        AddRecordAccess(services);
        AddUseCases(services);
        services.AddScoped<IStartupTask, AttachmentCatalogStartupCheck>();
        // scaffold:registrations
        return services;
    }

    /// <summary>Maps nothing extra: every Attachments endpoint is a controller, found by the kernel.</summary>
    public static IEndpointRouteBuilder UseAttachmentsModule(this IEndpointRouteBuilder endpoints)
    {
        ArgumentNullException.ThrowIfNull(endpoints);
        return endpoints;
    }

    /// <summary>
    /// The record accesses the gate knows: every registered <see cref="IAttachableRecordAccess"/> (owning modules), every
    /// access an <see cref="IAttachableRecordAccessSource"/> serves (the Records module: one per registered record type),
    /// plus <see cref="DemoRecordAccess"/> when the host runs in Development.
    /// </summary>
    public static IEnumerable<IAttachableRecordAccess> RecordAccessesOf(IServiceProvider provider)
    {
        ArgumentNullException.ThrowIfNull(provider);
        var registered = provider.GetServices<IAttachableRecordAccess>()
            .Concat(provider.GetServices<IAttachableRecordAccessSource>().SelectMany(source => source.Accesses));
        return provider.GetRequiredService<IHostEnvironment>().IsDevelopment()
            ? registered.Append(provider.GetRequiredService<DemoRecordAccess>())
            : registered;
    }

    private static void AddOptions(IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<AttachmentsOptions>().Bind(configuration.GetSection(AttachmentsOptions.SectionName)).ValidateOnStart();
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IValidateOptions<AttachmentsOptions>, AttachmentsOptionsValidator>());
        services.AddOptions<UploadTicketRingOptions>().Bind(configuration.GetSection(UploadTicketRingOptions.SectionName)).ValidateOnStart();
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IValidateOptions<UploadTicketRingOptions>, UploadTicketRingOptionsValidator>());
        services.Configure<CodelabErrorsOptions>(errors => errors.AddAttachmentsFailures());
    }

    private static void AddSecurity(IServiceCollection services)
    {
        services.AddSingleton<ReceiptVerifier>();
        services.AddSingleton<IAttachmentReceiptVerifier>(provider => provider.GetRequiredService<ReceiptVerifier>());
        services.AddSingleton<UploadTicketRing>();
        services.AddSingleton<IUploadTicketIssuer, UploadTicketIssuer>();
    }

    private static void AddStorage(IServiceCollection services, IConfiguration configuration)
    {
        services.AddHttpClient<IAttachmentStorageClient, HttpAttachmentStorageClient>((provider, client) =>
            {
                client.BaseAddress = provider.GetRequiredService<IOptions<AttachmentsOptions>>().Value.ServiceRoot();
                client.Timeout = HttpAttachmentStorageClient.RequestTimeout;
            })
            .AddCodelabServiceAssertion(configuration);
        services.AddScoped<IAttachmentFileCleanup, OutboxAttachmentFileCleanup>();
        services.AddOutboxHandler<DeleteAttachmentFileHandler>();
    }

    private static void AddRecordAccess(IServiceCollection services)
    {
        services.AddScoped<DemoRecordAccess>();
        services.AddScoped(provider => new AttachableRecords(RecordAccessesOf(provider)));
        services.AddScoped<AttachmentTargets>();
        services.AddScoped<AttachmentActor>();
    }

    private static void AddUseCases(IServiceCollection services)
    {
        services.AddSingleton<IValidator<IssueUploadTicketCommand>, IssueUploadTicketCommandValidator>();
        services.AddSingleton<IValidator<LinkUploadedFileCommand>, LinkUploadedFileCommandValidator>();
        services.AddScoped<ListAttachmentTypes>();
        services.AddScoped<ListRecordAttachments>();
        services.AddSingleton<UploadScopes>();
        services.AddSingleton<ScopedReceipts>();
        services.AddScoped<IssueUploadTicket>();
        services.AddScoped<LinkUploadedFile>();
        services.AddScoped<DownloadAttachment>();
        services.AddScoped<RemoveAttachment>();
        services.AddScoped<IRequiredAttachments, RequiredAttachmentsCheck>();
    }
}
