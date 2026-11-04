using Codelab.Integration.Outbound;
using Polly;
using Polly.Retry;

namespace Codelab.Integration.Resilience;

/// <summary>
/// Builds the per-client pipeline, outermost first: total timeout, retry, attempt timeout. The total timeout bounds
/// every attempt and back-off delay together; the attempt timeout bounds one try, so a retry follows a slow attempt.
/// </summary>
internal static class OutboundResiliencePipeline
{
    public static void Configure(ResiliencePipelineBuilder<HttpResponseMessage> pipeline, OutboundClientOptions options, TimeProvider timeProvider)
    {
        pipeline.AddTimeout(options.Timeout.TotalTimeout);
        if (options.Retry.MaxRetries > 0)
        {
            pipeline.AddRetry(CreateRetry(options.Retry, timeProvider));
        }

        pipeline.AddTimeout(options.Timeout.AttemptTimeout);
    }

    internal static RetryStrategyOptions<HttpResponseMessage> CreateRetry(OutboundRetryOptions retry, TimeProvider timeProvider) => new()
    {
        MaxRetryAttempts = retry.MaxRetries,
        Delay = retry.BaseDelay,
        MaxDelay = retry.MaxDelay,
        BackoffType = DelayBackoffType.Exponential,
        UseJitter = retry.UseJitter,
        ShouldHandle = arguments => ValueTask.FromResult(RetryDecision.ShouldRetry(
            request: arguments.Outcome.Result?.RequestMessage ?? arguments.Context.GetRequestMessage(),
            response: arguments.Outcome.Result,
            exception: arguments.Outcome.Exception)),
        DelayGenerator = arguments => ValueTask.FromResult(RetryAfterDelay.From(arguments.Outcome.Result, retry.MaxDelay, timeProvider)),
    };
}
