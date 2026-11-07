using Codelab.Observability;
using Codelab.OpsConsole.Api.Authentication;
using Codelab.OpsConsole.Api.Endpoints;
using Codelab.OpsConsole.Api.Upstream;
using Codelab.OpsConsole.Application.Ports;
using Codelab.OpsConsole.Application.ReferenceCodes;
using Codelab.Security.ServiceAuth;
using Microsoft.Extensions.Options;

namespace Codelab.OpsConsole.Api.Hosting;

/// <summary>The host wiring Program.cs calls: services (<see cref="AddOpsConsoleHost"/>) and the pipeline (<see cref="UseOpsConsolePipeline"/>).</summary>
public static class OpsConsoleHostExtensions
{
    /// <summary>
    /// Registers the validated <see cref="OpsConsoleOptions"/>, operator authentication and the operator policy, the
    /// <c>/api/ops</c> client (service assertion per request) and the Seq reader, and the reference-code lookup. Call it
    /// after <c>AddCodelabSecurity</c> (protocol and clock) and <c>AddCodelabObservability</c>.
    /// </summary>
    public static IHostApplicationBuilder AddOpsConsoleHost(this IHostApplicationBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        var services = builder.Services;
        var section = builder.Configuration.GetSection(OpsConsoleOptions.SectionName);
        services.AddOptions<OpsConsoleOptions>().Bind(section).ValidateOnStart();
        services.AddSingleton<IValidateOptions<OpsConsoleOptions>, OpsConsoleOptionsValidator>();
        services.AddOperatorAuthentication(section.Get<OpsConsoleOptions>() ?? new OpsConsoleOptions(), builder.Environment);
        services.AddOpsApiClient(builder.Configuration);
        services.AddSeqLogEventSource();
        services.AddTransient<LookUpReferenceCode>();
        return builder;
    }

    /// <summary>Routing, then authentication and authorization.</summary>
    public static IApplicationBuilder UseOpsConsolePipeline(this IApplicationBuilder app)
    {
        ArgumentNullException.ThrowIfNull(app);
        app.UseRouting();
        app.UseAuthentication();
        app.UseAuthorization();
        return app;
    }

    /// <summary>Maps the <c>/ops-api</c> endpoints (<see cref="OpsConsoleEndpoints"/>).</summary>
    public static IEndpointRouteBuilder MapOpsConsoleEndpoints(this IEndpointRouteBuilder endpoints)
    {
        ArgumentNullException.ThrowIfNull(endpoints);
        OpsConsoleEndpoints.Map(endpoints);
        return endpoints;
    }

    private static void AddOpsApiClient(this IServiceCollection services, IConfiguration configuration) =>
        services.AddHttpClient<IOpsApiClient, OpsApiClient>(OpsApiClient.HttpClientName, static (provider, client) =>
            {
                var api = provider.GetRequiredService<IOptions<OpsConsoleOptions>>().Value.Api;
                client.BaseAddress = UpstreamAddress.AsBaseAddress(api.BaseUrl!);
                client.Timeout = TimeSpan.FromSeconds(api.TimeoutSeconds);
            })
            .AsInternalService()
            .AddCodelabServiceAssertion(configuration);

    private static void AddSeqLogEventSource(this IServiceCollection services) =>
        services.AddHttpClient<ILogEventSource, SeqLogEventSource>(SeqLogEventSource.HttpClientName, static (provider, client) =>
            {
                var seq = provider.GetRequiredService<IOptions<OpsConsoleOptions>>().Value.Seq;
                client.Timeout = TimeSpan.FromSeconds(seq.TimeoutSeconds);
                if (seq.IsConfigured)
                {
                    client.BaseAddress = UpstreamAddress.AsBaseAddress(seq.BaseUrl!);
                }
            })
            .AsExternalService();
}
