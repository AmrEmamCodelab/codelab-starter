using Codelab.Integration.Outbound;
using Microsoft.Extensions.DependencyInjection;

namespace Codelab.Integration.Tests.Support;

/// <summary>The typed client the tests register.</summary>
internal sealed class PingClient(HttpClient http)
{
    public HttpClient Http { get; } = http;
}

/// <summary>Builds a service provider with one Codelab outbound client.</summary>
internal static class OutboundTestHost
{
    public const string ClientName = "upstream";
    public static readonly Uri StubAddress = new("http://upstream.example.test/");

    public static ServiceProvider Build(
        Action<OutboundClientOptions> configure,
        Action<IHttpClientBuilder>? client = null,
        Action<IServiceCollection>? extra = null)
    {
        var services = new ServiceCollection();
        var builder = services.AddCodelabOutboundClient<PingClient>(ClientName, configure);
        client?.Invoke(builder);
        extra?.Invoke(services);
        return services.BuildServiceProvider(new ServiceProviderOptions { ValidateOnBuild = true, ValidateScopes = true });
    }

    /// <summary>A client whose primary handler is <paramref name="stub"/> instead of the network.</summary>
    public static ServiceProvider BuildOnStub(StubHandler stub, int maxRetries, Action<OutboundClientOptions>? tweak = null) =>
        Build(
            options =>
            {
                StubOptions(maxRetries)(options);
                tweak?.Invoke(options);
            },
            client: builder => builder.ConfigurePrimaryHttpMessageHandler(() => stub));

    /// <summary>Plain-HTTP stub upstream with fast, deterministic retries.</summary>
    public static Action<OutboundClientOptions> StubOptions(int maxRetries) => options =>
    {
        options.BaseAddress = StubAddress;
        options.AllowPlainHttp = true;
        options.Retry.MaxRetries = maxRetries;
        options.Retry.BaseDelay = TimeSpan.FromMilliseconds(1);
        options.Retry.MaxDelay = TimeSpan.FromMilliseconds(5);
        options.Retry.UseJitter = false;
    };

    public static HttpClient ClientOf(ServiceProvider provider) => provider.GetRequiredService<PingClient>().Http;
}

/// <summary>A shared counter, so a handler instance per chain can still report to the test.</summary>
internal sealed class CallCounter
{
    private int _calls;

    public int Calls => Volatile.Read(ref _calls);

    public void Increment() => Interlocked.Increment(ref _calls);
}

/// <summary>Counts the requests that pass through it.</summary>
internal sealed class CountingHandler(CallCounter counter) : DelegatingHandler
{
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        counter.Increment();
        return base.SendAsync(request, cancellationToken);
    }
}
