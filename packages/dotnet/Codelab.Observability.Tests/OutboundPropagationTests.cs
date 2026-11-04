using System.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Http;
using Microsoft.Extensions.Options;

namespace Codelab.Observability.Tests;

/// <summary>Trace and correlation context leave the host only to internal services, and certificate validation stays on.</summary>
public sealed class OutboundPropagationTests
{
    [Fact]
    public void The_internal_handler_keeps_platform_certificate_validation()
    {
        using var handler = OutboundPropagation.CreateInternalServiceHandler();

        Assert.Null(handler.SslOptions.RemoteCertificateValidationCallback);
        Assert.Same(PropagationScope.InternalPropagator, handler.ActivityHeadersPropagator);
    }

    [Fact]
    public void The_internal_propagator_sends_trace_context_and_no_baggage()
    {
        using var activity = new Activity("outbound").SetIdFormat(ActivityIdFormat.W3C).AddBaggage("user", "jane").Start();
        var sent = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);

        PropagationScope.InternalPropagator.Inject(activity, carrier: null, (_, name, value) => sent[name] = value);

        Assert.True(sent.ContainsKey(PropagationScope.TraceParentHeader));
        Assert.All(sent.Keys, key => Assert.Contains(key, PropagationScope.InternalPropagator.Fields));
        Assert.DoesNotContain(sent.Values, value => value?.Contains("jane", StringComparison.Ordinal) == true);
        Assert.Null(PropagationScope.InternalPropagator.ExtractBaggage(carrier: null, getter: null));
    }

    [Fact]
    public void The_external_propagator_sends_nothing()
    {
        using var activity = new Activity("outbound").SetIdFormat(ActivityIdFormat.W3C).Start();
        var sent = new Dictionary<string, string?>();

        PropagationScope.ExternalPropagator.Inject(activity, carrier: null, (_, name, value) => sent[name] = value);

        Assert.Empty(sent);
    }

    [Fact]
    public void Deny_by_default_makes_the_process_default_send_nothing()
    {
        PropagationScope.DenyOutboundByDefault();

        Assert.Same(PropagationScope.ExternalPropagator, DistributedContextPropagator.Current);
    }

    [Fact]
    public async Task An_internal_client_forwards_the_current_correlation_id()
    {
        var recorder = new RecordingHandler();
        using var provider = ClientServices(builder => builder.AsInternalService().AddHttpMessageHandler(() => recorder));
        using var client = provider.GetRequiredService<IHttpClientFactory>().CreateClient(ClientName);

        using (CorrelationScope.Begin("9c1e5b7a20d84f6e8a3b1c2d4e5f6a7b"))
        {
            using var response = await client.GetAsync(new Uri("https://internal.example.test/ping"));
        }

        Assert.Equal("9c1e5b7a20d84f6e8a3b1c2d4e5f6a7b", recorder.LastRequest!.Headers.GetValues(CorrelationId.Header).Single());
    }

    [Fact]
    public void An_external_client_gets_the_no_output_propagator_on_its_sockets_handler()
    {
        using var provider = ClientServices(builder => builder
            .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler())
            .AsExternalService());
        var options = provider.GetRequiredService<IOptionsMonitor<HttpClientFactoryOptions>>().Get(ClientName);
        var primary = new HttpMessageHandlerBuilderStub(provider);

        foreach (var action in options.HttpMessageHandlerBuilderActions)
        {
            action(primary);
        }

        var sockets = Assert.IsType<SocketsHttpHandler>(primary.PrimaryHandler);
        Assert.Same(PropagationScope.ExternalPropagator, sockets.ActivityHeadersPropagator);
        Assert.Null(sockets.SslOptions.RemoteCertificateValidationCallback);
    }

    private const string ClientName = "probe";

    private static ServiceProvider ClientServices(Action<IHttpClientBuilder> configure)
    {
        var services = new ServiceCollection();
        configure(services.AddHttpClient(ClientName));
        return services.BuildServiceProvider();
    }

    private sealed class RecordingHandler : DelegatingHandler
    {
        public HttpRequestMessage? LastRequest { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            LastRequest = request;
            return Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.OK));
        }
    }

    private sealed class HttpMessageHandlerBuilderStub(IServiceProvider services) : HttpMessageHandlerBuilder
    {
        public override string? Name { get; set; } = ClientName;

        public override HttpMessageHandler PrimaryHandler { get; set; } = new HttpClientHandler();

        public override IList<DelegatingHandler> AdditionalHandlers { get; } = [];

        public override IServiceProvider Services => services;

        public override HttpMessageHandler Build() => CreateHandlerPipeline(PrimaryHandler, AdditionalHandlers);
    }
}
