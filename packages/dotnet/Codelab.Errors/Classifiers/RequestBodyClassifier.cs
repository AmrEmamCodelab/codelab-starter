using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

namespace Codelab.Errors.Classifiers;

/// <summary>
/// A request body the server refused to read or could not parse: Kestrel's <see cref="BadHttpRequestException"/>
/// (413 over the body limit, 400 for a malformed body or an unparseable minimal-API argument) keeps the status
/// Kestrel chose; a body-buffering overflow (<see cref="IOException"/> with <see cref="BufferLimitMessage"/>) is 413.
/// Information: the caller's to fix, and it says nothing about the server's health.
/// <para>
/// The <see cref="IOException"/> match is narrow on purpose and fails toward 500: claiming every I/O fault would
/// report a disk or socket failure as "your request is too large".
/// </para>
/// </summary>
public sealed class RequestBodyClassifier : IFailureClassifier
{
    /// <summary>What the framework's buffering stream says when a body passes its limit (no subtype, no code to key on).</summary>
    public const string BufferLimitMessage = "Buffer limit exceeded.";

    /// <inheritdoc />
    public Failure? Classify(Exception exception, HttpContext? context) => exception switch
    {
        BadHttpRequestException bad => Describe(bad.StatusCode),
        IOException io when io.Message.Contains(BufferLimitMessage, StringComparison.Ordinal)
            => Describe(StatusCodes.Status413PayloadTooLarge),
        _ => null,
    };

    private static Failure Describe(int status)
    {
        var description = FrameworkStatusCodes.Describe(status);
        return new Failure(status, description.Code, description.Detail, LogLevel.Information);
    }
}
