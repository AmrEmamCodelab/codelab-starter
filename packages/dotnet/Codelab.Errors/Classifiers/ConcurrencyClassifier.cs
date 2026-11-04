using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Codelab.Errors.Classifiers;

/// <summary>
/// 409 <see cref="ErrorCodes.Concurrency"/> for a lost update (<see cref="DbUpdateConcurrencyException"/>), so the
/// client can offer "reload and retry". MUST precede <see cref="TransientFaultClassifier"/> in the table: the
/// exception derives from <see cref="DbUpdateException"/>, and "503, please retry" is harmful advice for a lost update.
/// </summary>
public sealed class ConcurrencyClassifier : IFailureClassifier
{
    internal const string Detail = "This record changed while you were working on it. Reload and try again.";

    /// <inheritdoc />
    public Failure? Classify(Exception exception, HttpContext? context) =>
        exception is DbUpdateConcurrencyException
            ? new Failure(StatusCodes.Status409Conflict, ErrorCodes.Concurrency, Detail, LogLevel.Warning)
            : null;
}
