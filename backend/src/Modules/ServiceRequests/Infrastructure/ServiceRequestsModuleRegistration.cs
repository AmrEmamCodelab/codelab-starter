using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using Starter.Application.Authorization;
using Starter.Application.Records;
using Starter.Application.Validation;
using Starter.Infrastructure.Numbering;
using Starter.Infrastructure.Persistence;
using Starter.Infrastructure.Workflow;
using Starter.Modules.ServiceRequests.Application;
using Starter.Modules.ServiceRequests.Application.Access;
using Starter.Modules.ServiceRequests.Application.Drafts;
using Starter.Modules.ServiceRequests.Application.Workflow;
using Starter.Modules.ServiceRequests.Infrastructure.Persistence;

namespace Starter.Modules.ServiceRequests.Infrastructure;

/// <summary>
/// The ServiceRequests module's registrations (called by <see cref="ServiceRequestsModule"/>). The sample only
/// configures the engine: its workflow catalog, its record type, its SLA, its submit validator (the Records module serves its
/// attachment access from the record type registration), plus its own table and typed wizard endpoints.
/// </summary>
public static class ServiceRequestsModuleRegistration
{
    /// <summary>Registers the module's engine configuration, persistence, use cases and validators.</summary>
    public static IServiceCollection AddServiceRequestsModule(this IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IPermissionCatalogSource, ServiceRequestsPermissionCatalogSource>());
        services.TryAddSingleton(TimeProvider.System);
        AddEngineConfiguration(services);
        services.AddModuleDbContext<ServiceRequestsDbContext>(ServiceRequestsDbContext.SchemaName);
        services.AddScoped<IServiceRequestStore, EfServiceRequestStore>();
        services.AddSingleton<IServiceRequestCalendar>(provider => new AppTimeZoneCalendar(
            provider.GetRequiredService<TimeProvider>(),
            TimeZoneInfo.FindSystemTimeZoneById(provider.GetRequiredService<IOptions<NumberingOptions>>().Value.TimeZoneId)));
        AddUseCases(services);
        // scaffold:registrations
        return services;
    }

    /// <summary>Maps nothing extra: every ServiceRequests endpoint is a controller, found by the kernel.</summary>
    public static IEndpointRouteBuilder UseServiceRequestsModule(this IEndpointRouteBuilder endpoints)
    {
        ArgumentNullException.ThrowIfNull(endpoints);
        return endpoints;
    }

    private static void AddEngineConfiguration(IServiceCollection services)
    {
        services.AddRecordWorkflow<ServiceRequestWorkflowDefinition>();
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IRecordTypeRegistrationSource, ServiceRequestRecordTypeSource>());
        services.TryAddEnumerable(ServiceDescriptor.Singleton<ISlaPolicySource, ServiceRequestSlaPolicySource>());
        services.TryAddEnumerable(ServiceDescriptor.Scoped<IRecordSubmitValidator, ServiceRequestSubmitValidator>());
    }

    private static void AddUseCases(IServiceCollection services)
    {
        services.AddScoped<ServiceRequestAccess>();
        services.AddScoped<ServiceRequestCategories>();
        services.AddScoped<ServiceRequestViews>();
        services.AddSingleton<IValidator<SaveServiceRequestDraftCommand>, SaveServiceRequestDraftValidator>();
        services.AddScoped<CreateServiceRequestDraft>();
        services.AddScoped<GetServiceRequestDraft>();
        services.AddScoped<SaveServiceRequestDraft>();
        services.AddScoped<SubmitServiceRequest>();
        services.AddScoped<GetServiceRequestDetails>();
    }
}

/// <summary>Today in the app time zone (the Numbering time zone, <c>Numbering:TimeZoneId</c>), from <see cref="TimeProvider"/>.</summary>
public sealed class AppTimeZoneCalendar(TimeProvider timeProvider, TimeZoneInfo timeZone) : IServiceRequestCalendar
{
    /// <inheritdoc />
    public DateOnly Today() => DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(timeProvider.GetUtcNow(), timeZone).DateTime);
}
