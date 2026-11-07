using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using Starter.Application.SingleSignOn;
using Starter.Infrastructure.Jobs;
using Starter.Infrastructure.Persistence;
using Starter.Modules.Ops.Api;
using Starter.Modules.Ops.Application;
using Starter.Modules.Ops.Application.Access;
using Starter.Modules.Ops.Application.Certificates;
using Starter.Modules.Ops.Application.Health;
using Starter.Modules.Ops.Application.Jobs;
using Starter.Modules.Ops.Application.ReferenceCodes;
using Starter.Modules.Ops.Infrastructure.Authorization;
using Starter.Modules.Ops.Infrastructure.Certificates;
using Starter.Modules.Ops.Infrastructure.Health;
using Starter.Modules.Ops.Infrastructure.Jobs;
using Starter.Modules.Ops.Infrastructure.Options;
using Starter.Modules.Ops.Infrastructure.Persistence;
using Starter.Modules.Ops.Infrastructure.ReferenceCodes;

namespace Starter.Modules.Ops.Infrastructure;

/// <summary>The Ops module's registrations (called by <see cref="OpsModule"/>).</summary>
public static class OpsModuleRegistration
{
    /// <summary>The named HTTP client of the readiness probes and the JWKS read (validated TLS, no proxy tricks).</summary>
    public const string HttpClientName = "starter-ops";

    /// <summary>
    /// Registers the options (<c>Codelab:Ops</c>, validated on start), the Ops caller policy and scheme, the
    /// <c>ops</c> schema, the use cases, the probes and certificate sources, the reference-code capture and its
    /// purge job. The readiness probes are read from configuration once, here: changing them needs a restart.
    /// </summary>
    public static IServiceCollection AddOpsModule(this IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        var section = configuration.GetSection(OpsOptions.SectionName);
        var snapshot = section.Get<OpsOptions>() ?? new OpsOptions();
        services.AddOptions<OpsOptions>().Bind(section).ValidateOnStart();
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IValidateOptions<OpsOptions>, OpsOptionsValidator>());
        services.AddSingleton(provider => provider.GetRequiredService<IOptions<OpsOptions>>().Value.ToSettings());
        services.AddSingleton(provider => new ProbeBudget(
            provider.GetRequiredService<TimeProvider>(),
            TimeSpan.FromSeconds(provider.GetRequiredService<IOptions<OpsOptions>>().Value.ProbeTimeoutSeconds)));
        services.AddHttpClient(HttpClientName);
        services.AddOpsCallerAuthorization(configuration, isOpen: snapshot.IsOpen);
        services.AddModuleDbContext<OpsDbContext>(OpsDbContext.SchemaName);
        AddUseCases(services);
        AddProbes(services, snapshot);
        AddCertificateSources(services);
        AddReferenceCodeCapture(services);
        return services;
    }

    /// <summary>Maps nothing extra: every Ops endpoint is a controller, found by the kernel.</summary>
    public static IEndpointRouteBuilder UseOpsModule(this IEndpointRouteBuilder endpoints)
    {
        ArgumentNullException.ThrowIfNull(endpoints);
        return endpoints;
    }

    private static void AddUseCases(IServiceCollection services)
    {
        services.AddScoped<GetSystemHealth>();
        services.AddScoped<ListJobStatuses>();
        services.AddScoped<ListCertificates>();
        services.AddScoped<FindReferenceCode>();
        services.AddScoped<RecordOperatorAccess>();
        services.AddScoped<OperatorAccessFilter>();
        services.AddScoped<IJobStatusSource, JobCatalogStatusSource>();
        services.AddScoped<IOperatorAccessLog, SqlOperatorAccessLog>();
        services.AddScoped<IReferenceCodeSightings, SqlReferenceCodeSightings>();
    }

    private static void AddProbes(IServiceCollection services, OpsOptions snapshot)
    {
        services.AddScoped<IComponentProbe, DatabaseProbe>();
        foreach (var probe in snapshot.ReadinessProbes.Where(probe => Uri.IsWellFormedUriString(probe.Url, UriKind.Absolute)))
        {
            var name = probe.Name ?? string.Empty;
            var url = new Uri(probe.Url!);
            services.AddScoped<IComponentProbe>(provider => new HttpReadinessProbe(
                name,
                url,
                provider.GetRequiredService<IHttpClientFactory>().CreateClient(HttpClientName),
                provider.GetRequiredService<ProbeBudget>()));
        }
    }

    private static void AddCertificateSources(IServiceCollection services)
    {
        services.AddScoped<ICertificateSource, ConfiguredCertificateSource>();
        services.AddScoped<ICertificateSource>(provider => new IdentityProviderCertificateSource(
            provider.GetRequiredService<IHttpClientFactory>().CreateClient(HttpClientName),
            provider.GetRequiredService<IOptions<OpsOptions>>(),
            provider.GetRequiredService<ProbeBudget>(),
            provider.GetService<IIdentityProviderKeySet>()));
    }

    private static void AddReferenceCodeCapture(IServiceCollection services)
    {
        services.AddSingleton<ReferenceCodeInbox>();
        services.AddSingleton<ReferenceCodeIndexWriter>();
        services.AddHostedService(provider => provider.GetRequiredService<ReferenceCodeIndexWriter>());
        services.AddTransient<IStartupFilter, ReferenceCodeCaptureStartupFilter>();
        services.AddStarterJob<ReferenceCodePurgeJob>();
    }
}
