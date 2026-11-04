using Codelab.Integration.Classification;

namespace Codelab.Integration.Resilience;

/// <summary>The retry predicate: a retry-safe request after a transient failure, nothing else.</summary>
internal static class RetryDecision
{
    public static bool ShouldRetry(HttpRequestMessage? request, HttpResponseMessage? response, Exception? exception)
    {
        if (!RetrySafety.IsRetrySafe(request))
        {
            return false;
        }

        var failure = exception is not null
            ? OutboundFailureClassifier.Classify(exception)
            : response is null ? null : OutboundFailureClassifier.Classify(response);
        return failure is { IsTransient: true };
    }
}
