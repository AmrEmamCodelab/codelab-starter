using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

namespace Codelab.Errors;

/// <summary>
/// How one exception is reported: the status, its machine code, the English fallback sentence, how loudly it is
/// logged and, for a transient failure only, how long the caller should wait. It holds a DECISION and performs no
/// side effect, which is what lets a classifier be tested with a plain assertion and no host.
/// </summary>
/// <param name="Status">The HTTP status code.</param>
/// <param name="Code">The machine code (<see cref="ErrorCodes"/> or a module's code); what the client branches on.</param>
/// <param name="Detail">
/// A vetted English fallback sentence for API clients and tests. It reaches the wire, so it never quotes an
/// exception message or a value; the SPA localises from <paramref name="Code"/>.
/// </param>
/// <param name="Level">The severity this failure class deserves: Information or Warning for 4xx, Error for a bug.</param>
/// <param name="RetryAfterSeconds">Written as a <c>Retry-After</c> header when present.</param>
public readonly record struct Failure(int Status, string Code, string Detail, LogLevel Level, int? RetryAfterSeconds = null)
{
    /// <summary>
    /// The bug class: a 5xx rated above Warning. Only these mark the request's span failed; every other failure is an
    /// expected outcome.
    /// </summary>
    public bool IsServerFault => Status >= StatusCodes.Status500InternalServerError && Level > LogLevel.Warning;
}

/// <summary>
/// One failure class and the single question it answers: is this exception mine, and if so how is it reported?
/// <c>null</c> means "not mine" and passes the exception to the next classifier in the ordered table
/// (<see cref="CodelabErrorsOptions.Classifiers"/>).
/// <para>
/// Implementations are PURE: no logging, no response writing, no I/O, and they must not read the
/// <c>HttpContext</c> beyond what is safe without one (the job failure filter passes <c>null</c>). Everything that
/// happens to a request is done once by <see cref="GlobalExceptionHandler"/>.
/// </para>
/// </summary>
public interface IFailureClassifier
{
    /// <summary>The failure for <paramref name="exception"/>, or null when this classifier does not claim it.</summary>
    Failure? Classify(Exception exception, HttpContext? context);
}
