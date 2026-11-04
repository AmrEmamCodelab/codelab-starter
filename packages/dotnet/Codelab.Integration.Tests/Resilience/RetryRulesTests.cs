using System.Net;
using System.Net.Http.Headers;
using Codelab.Integration.Outbound;
using Codelab.Integration.Resilience;
using Microsoft.Extensions.Time.Testing;

namespace Codelab.Integration.Tests.Resilience;

/// <summary>The retry predicate (fail-closed retry safety) and the Retry-After reading.</summary>
public sealed class RetryRulesTests
{
    private static readonly TimeSpan MaxDelay = TimeSpan.FromSeconds(10);

    [Theory]
    [InlineData("GET", true)]
    [InlineData("HEAD", true)]
    [InlineData("OPTIONS", true)]
    [InlineData("POST", false)]
    [InlineData("PUT", false)]
    [InlineData("PATCH", false)]
    [InlineData("DELETE", false)]
    public void Only_safe_methods_are_retry_safe_without_a_mark(string method, bool safe)
    {
        using var request = new HttpRequestMessage(new HttpMethod(method), "/x");

        Assert.Equal(safe, RetrySafety.IsRetrySafe(request));
    }

    [Fact]
    public void A_marked_request_is_retry_safe_and_an_unknown_request_is_not()
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "/x").MarkIdempotent();

        Assert.True(request.IsMarkedIdempotent());
        Assert.True(RetrySafety.IsRetrySafe(request));
        Assert.False(RetrySafety.IsRetrySafe(request: null));
    }

    [Fact]
    public void A_transient_status_on_a_safe_request_is_retried()
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "/x");
        using var response = new HttpResponseMessage(HttpStatusCode.ServiceUnavailable);

        Assert.True(RetryDecision.ShouldRetry(request, response, exception: null));
    }

    [Fact]
    public void A_transient_status_on_an_unmarked_post_is_not_retried()
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "/x");
        using var response = new HttpResponseMessage(HttpStatusCode.ServiceUnavailable);

        Assert.False(RetryDecision.ShouldRetry(request, response, exception: null));
    }

    [Theory]
    [InlineData(HttpStatusCode.OK)]
    [InlineData(HttpStatusCode.BadRequest)]
    [InlineData(HttpStatusCode.Unauthorized)]
    [InlineData(HttpStatusCode.NotImplemented)]
    public void Success_and_permanent_failures_are_not_retried(HttpStatusCode status)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "/x");
        using var response = new HttpResponseMessage(status);

        Assert.False(RetryDecision.ShouldRetry(request, response, exception: null));
    }

    [Fact]
    public void Exceptions_decide_by_their_classification()
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "/x");

        Assert.True(RetryDecision.ShouldRetry(request, response: null, new HttpRequestException(HttpRequestError.ConnectionError)));
        Assert.False(RetryDecision.ShouldRetry(request, response: null, new HttpRequestException(HttpRequestError.SecureConnectionError)));
        Assert.False(RetryDecision.ShouldRetry(request, response: null, new OperationCanceledException()));
        Assert.False(RetryDecision.ShouldRetry(request, response: null, exception: null));
    }

    [Fact]
    public void Retry_after_as_a_delta_is_honoured_and_capped()
    {
        using var shortWait = Throttled(TimeSpan.FromSeconds(3));
        using var longWait = Throttled(TimeSpan.FromMinutes(5));

        Assert.Equal(TimeSpan.FromSeconds(3), RetryAfterDelay.From(shortWait, MaxDelay, TimeProvider.System));
        Assert.Equal(MaxDelay, RetryAfterDelay.From(longWait, MaxDelay, TimeProvider.System));
    }

    [Fact]
    public void Retry_after_as_a_date_is_measured_against_the_injected_clock()
    {
        var clock = new FakeTimeProvider(new DateTimeOffset(2026, 11, 4, 8, 0, 0, TimeSpan.Zero));
        using var response = new HttpResponseMessage(HttpStatusCode.ServiceUnavailable);
        response.Headers.RetryAfter = new RetryConditionHeaderValue(clock.GetUtcNow().AddSeconds(4));

        Assert.Equal(TimeSpan.FromSeconds(4), RetryAfterDelay.From(response, MaxDelay, clock));

        clock.Advance(TimeSpan.FromSeconds(5));
        Assert.Null(RetryAfterDelay.From(response, MaxDelay, clock));
    }

    [Fact]
    public void Retry_after_is_ignored_when_absent_or_on_other_statuses()
    {
        using var noHeader = new HttpResponseMessage(HttpStatusCode.TooManyRequests);
        using var wrongStatus = new HttpResponseMessage(HttpStatusCode.InternalServerError);
        wrongStatus.Headers.RetryAfter = new RetryConditionHeaderValue(TimeSpan.FromSeconds(2));

        Assert.Null(RetryAfterDelay.From(noHeader, MaxDelay, TimeProvider.System));
        Assert.Null(RetryAfterDelay.From(wrongStatus, MaxDelay, TimeProvider.System));
        Assert.Null(RetryAfterDelay.From(response: null, MaxDelay, TimeProvider.System));
    }

    private static HttpResponseMessage Throttled(TimeSpan delta)
    {
        var response = new HttpResponseMessage(HttpStatusCode.TooManyRequests);
        response.Headers.RetryAfter = new RetryConditionHeaderValue(delta);
        return response;
    }
}
