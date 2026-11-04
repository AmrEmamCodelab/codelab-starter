using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

namespace Codelab.Errors.Classifiers;

/// <summary>
/// 502 <see cref="ErrorCodes.Upstream"/> for an <see cref="UpstreamUnavailableException"/>: not this server's fault
/// and not the caller's. Warning rather than Error, because the action is "check the dependency".
/// </summary>
public sealed class UpstreamUnavailableClassifier : IFailureClassifier
{
    internal const string Detail = "A dependency is temporarily unavailable. Please try again.";

    /// <inheritdoc />
    public Failure? Classify(Exception exception, HttpContext? context) =>
        exception is UpstreamUnavailableException
            ? new Failure(StatusCodes.Status502BadGateway, ErrorCodes.Upstream, Detail, LogLevel.Warning)
            : null;
}
