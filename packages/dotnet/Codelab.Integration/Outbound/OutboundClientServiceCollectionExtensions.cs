using Codelab.Integration.Certificates;
using Codelab.Integration.Resilience;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;

namespace Codelab.Integration.Outbound;

/// <summary>Registers Codelab outbound clients.</summary>
public static class OutboundClientServiceCollectionExtensions
{
    /// <summary>
    /// Registers <typeparamref name="TClient"/> as a typed client named <paramref name="name"/>: handler-slot handlers
    /// (outermost), then total timeout, retry and attempt timeout, then a primary handler that validates the server
    /// certificate. Options are validated at start-up. Handlers added to the returned builder run inside the policy,
    /// once per attempt.
    /// </summary>
    public static IHttpClientBuilder AddCodelabOutboundClient<TClient>(this IServiceCollection services, string name, Action<OutboundClientOptions> configure)
        where TClient : class
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(configure);
        services.AddOptions<OutboundClientOptions>(name).Configure(configure).ValidateOnStart();
        return AddClient<TClient>(services, name);
    }

    /// <summary>
    /// Same as the overload taking a delegate, with the options bound from <paramref name="section"/> (the host passes
    /// its typed section, for example the section named by its own options class's SectionName constant).
    /// </summary>
    public static IHttpClientBuilder AddCodelabOutboundClient<TClient>(this IServiceCollection services, string name, IConfiguration section)
        where TClient : class
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(section);
        services.AddOptions<OutboundClientOptions>(name).Bind(section).ValidateOnStart();
        return AddClient<TClient>(services, name);
    }

    /// <summary>
    /// Adds <typeparamref name="THandler"/> to the outermost slot of every Codelab outbound client (in registration
    /// order), for example a correlation-id handler. It runs once per call, outside the retry policy.
    /// </summary>
    public static IServiceCollection AddCodelabOutboundHandler<THandler>(this IServiceCollection services)
        where THandler : DelegatingHandler
    {
        ArgumentNullException.ThrowIfNull(services);
        services.TryAddTransient<THandler>();
        services.AddSingleton(new OutboundHandlerSlot(typeof(THandler)));
        return services;
    }

    private static IHttpClientBuilder AddClient<TClient>(IServiceCollection services, string name)
        where TClient : class
    {
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IValidateOptions<OutboundClientOptions>, OutboundClientOptionsValidator>());
        services.TryAddSingleton(TimeProvider.System);

        var builder = services.AddHttpClient<TClient>(name, (provider, client) =>
        {
            client.BaseAddress = OptionsOf(provider, name).BaseAddress;
            client.Timeout = Timeout.InfiniteTimeSpan;
        });
        builder.ConfigurePrimaryHttpMessageHandler(provider =>
            ServerCertificatePolicy.CreatePrimaryHandler(OptionsOf(provider, name).Certificate, provider.GetRequiredService<TimeProvider>()));
        builder.ConfigureAdditionalHttpMessageHandlers(InsertSlotHandlers);
        builder.AddResilienceHandler(name, (pipeline, context) =>
            OutboundResiliencePipeline.Configure(pipeline, OptionsOf(context.ServiceProvider, name), context.ServiceProvider.GetRequiredService<TimeProvider>()));
        return builder;
    }

    private static void InsertSlotHandlers(IList<DelegatingHandler> handlers, IServiceProvider provider)
    {
        var position = 0;
        foreach (var slot in provider.GetServices<OutboundHandlerSlot>())
        {
            handlers.Insert(position, (DelegatingHandler)provider.GetRequiredService(slot.HandlerType));
            position++;
        }
    }

    private static OutboundClientOptions OptionsOf(IServiceProvider provider, string name) =>
        provider.GetRequiredService<IOptionsMonitor<OutboundClientOptions>>().Get(name);
}
