using System.Net;
using Codelab.Integration.Classification;
using Codelab.Integration.Outbound;
using Codelab.Integration.Tests.Support;
using Polly.Timeout;

namespace Codelab.Integration.Tests.Resilience;

/// <summary>The registered client end to end on a stub primary handler: retry count, retry safety and timeouts.</summary>
public sealed class OutboundPipelineTests
{
    [Fact]
    public async Task A_get_is_retried_after_a_503_up_to_the_configured_count()
    {
        var stub = StubHandler.Always(HttpStatusCode.ServiceUnavailable);
        using var provider = OutboundTestHost.BuildOnStub(stub, maxRetries: 2);

        using var response = await OutboundTestHost.ClientOf(provider).GetAsync("/items");

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.Equal(3, stub.Attempts);
    }

    [Fact]
    public async Task A_get_succeeds_on_the_retry()
    {
        var stub = StubHandler.FirstThen(HttpStatusCode.BadGateway, HttpStatusCode.OK);
        using var provider = OutboundTestHost.BuildOnStub(stub, maxRetries: 2);

        using var response = await OutboundTestHost.ClientOf(provider).GetAsync("/items");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(2, stub.Attempts);
    }

    [Fact]
    public async Task An_unmarked_post_is_not_retried_after_a_503()
    {
        var stub = StubHandler.Always(HttpStatusCode.ServiceUnavailable);
        using var provider = OutboundTestHost.BuildOnStub(stub, maxRetries: 2);

        using var response = await OutboundTestHost.ClientOf(provider).PostAsync("/orders", new StringContent("{}"));

        Assert.Equal(1, stub.Attempts);
    }

    [Fact]
    public async Task A_post_marked_idempotent_is_retried()
    {
        var stub = StubHandler.Always(HttpStatusCode.ServiceUnavailable);
        using var provider = OutboundTestHost.BuildOnStub(stub, maxRetries: 2);
        using var request = new HttpRequestMessage(HttpMethod.Post, "/orders") { Content = new StringContent("{}") }.MarkIdempotent();

        using var response = await OutboundTestHost.ClientOf(provider).SendAsync(request);

        Assert.Equal(3, stub.Attempts);
    }

    [Theory]
    [InlineData(HttpStatusCode.BadRequest)]
    [InlineData(HttpStatusCode.Unauthorized)]
    [InlineData(HttpStatusCode.NotFound)]
    public async Task A_permanent_failure_is_not_retried(HttpStatusCode status)
    {
        var stub = StubHandler.Always(status);
        using var provider = OutboundTestHost.BuildOnStub(stub, maxRetries: 2);

        using var response = await OutboundTestHost.ClientOf(provider).GetAsync("/items");

        Assert.Equal(1, stub.Attempts);
    }

    [Fact]
    public async Task Zero_retries_means_exactly_one_attempt()
    {
        var stub = StubHandler.Always(HttpStatusCode.ServiceUnavailable);
        using var provider = OutboundTestHost.BuildOnStub(stub, maxRetries: 0);

        using var response = await OutboundTestHost.ClientOf(provider).GetAsync("/items");

        Assert.Equal(1, stub.Attempts);
    }

    [Fact]
    public async Task A_slow_attempt_times_out_and_the_next_attempt_answers()
    {
        var stub = new StubHandler(async (attempt, token) =>
        {
            if (attempt == 1)
            {
                await Task.Delay(Timeout.InfiniteTimeSpan, token);
            }

            return new HttpResponseMessage(HttpStatusCode.OK);
        });
        using var provider = OutboundTestHost.BuildOnStub(stub, maxRetries: 1, options => options.Timeout.AttemptTimeout = TimeSpan.FromMilliseconds(200));

        using var response = await OutboundTestHost.ClientOf(provider).GetAsync("/items");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(2, stub.Attempts);
    }

    [Fact]
    public async Task The_total_timeout_bounds_every_attempt_together()
    {
        var stub = new StubHandler(async (_, token) =>
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, token);
            return new HttpResponseMessage(HttpStatusCode.OK);
        });
        using var provider = OutboundTestHost.BuildOnStub(stub, maxRetries: 10, options =>
        {
            options.Timeout.AttemptTimeout = TimeSpan.FromMilliseconds(100);
            options.Timeout.TotalTimeout = TimeSpan.FromMilliseconds(350);
        });

        var exception = await Assert.ThrowsAsync<TimeoutRejectedException>(() => OutboundTestHost.ClientOf(provider).GetAsync("/items"));

        Assert.Equal(OutboundFailureKind.Timeout, OutboundFailureClassifier.Classify(exception).Kind);
        Assert.InRange(stub.Attempts, 2, 5);
    }
}
