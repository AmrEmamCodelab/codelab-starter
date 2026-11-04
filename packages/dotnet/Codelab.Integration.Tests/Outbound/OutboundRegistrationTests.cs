using System.Net;
using Codelab.Integration.Outbound;
using Codelab.Integration.Tests.Support;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Codelab.Integration.Tests.Outbound;

/// <summary>Registration: base address, options binding and validation, and the outermost handler slot.</summary>
public sealed class OutboundRegistrationTests
{
    private const string SlotHeader = "X-Test-Correlation";

    [Fact]
    public async Task The_typed_client_gets_the_base_address_and_no_client_timeout()
    {
        var stub = StubHandler.Always(HttpStatusCode.OK);
        using var provider = OutboundTestHost.BuildOnStub(stub, maxRetries: 0);
        var client = OutboundTestHost.ClientOf(provider);

        using var response = await client.GetAsync("items/1");

        Assert.Equal(OutboundTestHost.StubAddress, client.BaseAddress);
        Assert.Equal(Timeout.InfiniteTimeSpan, client.Timeout);
        Assert.Equal(new Uri(OutboundTestHost.StubAddress, "items/1"), Assert.Single(stub.Received).RequestUri);
    }

    [Fact]
    public async Task A_slot_handler_runs_once_per_call_and_outside_the_retries()
    {
        var stub = StubHandler.FirstThen(HttpStatusCode.ServiceUnavailable, HttpStatusCode.OK);
        var counter = new CallCounter();
        using var provider = OutboundTestHost.Build(
            OutboundTestHost.StubOptions(maxRetries: 2),
            client: builder => builder.ConfigurePrimaryHttpMessageHandler(() => stub),
            extra: services => services.AddSingleton(counter).AddCodelabOutboundHandler<HeaderSlotHandler>());

        using var response = await OutboundTestHost.ClientOf(provider).GetAsync("/items");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(1, counter.Calls);
        Assert.Equal(2, stub.Attempts);
        Assert.All(stub.Received, request => Assert.True(request.Headers.Contains(SlotHeader)));
    }

    [Fact]
    public async Task Options_are_bound_from_a_configuration_section()
    {
        var section = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["BaseAddress"] = "http://bound.example.test/",
            ["AllowPlainHttp"] = "true",
            ["Retry:MaxRetries"] = "1",
            ["Retry:BaseDelay"] = "00:00:00.001",
            ["Certificate:Mode"] = "1",
        }).Build();
        var services = new ServiceCollection();
        var stub = StubHandler.Always(HttpStatusCode.ServiceUnavailable);
        services.AddCodelabOutboundClient<PingClient>(OutboundTestHost.ClientName, section).ConfigurePrimaryHttpMessageHandler(() => stub);
        using var provider = services.BuildServiceProvider();

        using var response = await OutboundTestHost.ClientOf(provider).GetAsync("/items");

        Assert.Equal(2, stub.Attempts);
        Assert.Equal(new Uri("http://bound.example.test/"), OutboundTestHost.ClientOf(provider).BaseAddress);
    }

    [Fact]
    public void Invalid_options_fail_when_the_client_is_created()
    {
        using var provider = OutboundTestHost.Build(options => options.BaseAddress = new Uri("http://plain.example.test/"));

        var exception = Assert.Throws<OptionsValidationException>(() => OutboundTestHost.ClientOf(provider));

        Assert.Contains("https", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Invalid_options_fail_the_start_up_validation()
    {
        var services = new ServiceCollection();
        services.AddCodelabOutboundClient<PingClient>(OutboundTestHost.ClientName, options => options.Retry.MaxRetries = -1);
        using var provider = services.BuildServiceProvider();

        var exception = Assert.Throws<OptionsValidationException>(() => provider.GetRequiredService<IStartupValidator>().Validate());

        Assert.Single(provider.GetServices<IValidateOptions<OutboundClientOptions>>());
        Assert.Contains("MaxRetries", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Registration_guards_its_arguments()
    {
        var services = new ServiceCollection();

        Assert.Throws<ArgumentException>(() => services.AddCodelabOutboundClient<PingClient>(" ", _ => { }));
        Assert.Throws<ArgumentNullException>(() => services.AddCodelabOutboundClient<PingClient>("x", configure: null!));
        Assert.Throws<ArgumentNullException>(() => services.AddCodelabOutboundClient<PingClient>("x", section: null!));
    }

    private sealed class HeaderSlotHandler(CallCounter counter) : DelegatingHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            counter.Increment();
            request.Headers.TryAddWithoutValidation(SlotHeader, "call-1");
            return base.SendAsync(request, cancellationToken);
        }
    }
}
