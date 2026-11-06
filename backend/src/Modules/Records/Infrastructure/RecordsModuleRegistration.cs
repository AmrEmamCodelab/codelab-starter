using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Starter.Application.Attachments;
using Starter.Application.Authorization;
using Starter.Application.Records;
using Starter.Application.Validation;
using Starter.Infrastructure.Persistence;
using Starter.Infrastructure.Workflow;
using Starter.Modules.Records.Application;
using Starter.Modules.Records.Infrastructure.Persistence;

namespace Starter.Modules.Records.Infrastructure;

/// <summary>The Records module's registrations (called by <see cref="RecordsModule"/>).</summary>
public static class RecordsModuleRegistration
{
    /// <summary>
    /// Registers the permission catalog source, the workflow kernel (idempotent), the <c>records</c> schema, the kernel
    /// ports <see cref="IRecordLifecycle"/>, <see cref="IRecordDirectory"/>, <see cref="IRecordVisibility"/> and
    /// <see cref="IAttachableRecordAccessSource"/> (one attachable access per registered record type), the use cases and
    /// the record type check.
    /// </summary>
    public static IServiceCollection AddRecordsModule(this IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IPermissionCatalogSource, RecordsPermissionCatalogSource>());
        services.AddRecordWorkflowKernel();
        services.AddModuleDbContext<RecordsDbContext>(RecordsDbContext.SchemaName);
        services.AddScoped<IRecordStore, EfRecordStore>();
        services.TryAddSingleton<RecordTypes>();
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IHostedService, RecordTypesStartupCheck>());
        AddCore(services);
        AddReads(services);
        // scaffold:registrations
        return services;
    }

    /// <summary>Maps nothing extra: every Records endpoint is a controller, found by the kernel.</summary>
    public static IEndpointRouteBuilder UseRecordsModule(this IEndpointRouteBuilder endpoints)
    {
        ArgumentNullException.ThrowIfNull(endpoints);
        return endpoints;
    }

    private static void AddCore(IServiceCollection services)
    {
        services.AddScoped<RecordCallers>();
        services.AddScoped<RecordStandings>();
        services.AddScoped<RecordLoader>();
        services.AddScoped<TransitionEffects>();
        services.AddScoped<RecordCommits>();
        services.AddScoped<RecordSubmissions>();
        services.AddScoped<RecordTransitions>();
        services.AddScoped<RecordDrafts>();
        services.AddScoped<IRecordLifecycle, RecordLifecycle>();
        services.AddScoped<IRecordDirectory, RecordDirectory>();
        services.AddScoped<IRecordVisibility, RecordVisibility>();
        services.AddScoped<RecordAttachmentAccesses>();
        services.AddScoped<IAttachableRecordAccessSource>(provider => provider.GetRequiredService<RecordAttachmentAccesses>());
    }

    private static void AddReads(IServiceCollection services)
    {
        services.AddScoped<RecordUserNames>();
        services.AddScoped<RecordEnrichment>();
        services.AddScoped<GetRecordDetail>();
        services.AddScoped<GetRecordTimeline>();
        services.AddScoped<ListMyRecords>();
        services.AddScoped<IValidator<OversightQuery>, OversightQueryValidator>();
        services.AddScoped<ListRecordOversight>();
        services.AddScoped<IValidator<TransitionCommand>, TransitionCommandValidator>();
        services.AddScoped<TakeRecordTransition>();
    }
}
