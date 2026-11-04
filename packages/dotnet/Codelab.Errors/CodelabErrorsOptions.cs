using Codelab.Errors.Classifiers;
using Microsoft.Extensions.Logging;

namespace Codelab.Errors;

/// <summary>
/// What a host decides about its error contract, configured through <c>AddCodelabErrors(options =&gt; ...)</c>.
/// </summary>
public sealed class CodelabErrorsOptions
{
    /// <summary>
    /// The classifier table, IN ORDER: the first classifier that claims an exception wins, so registration order IS
    /// priority. The built-in entries come first, in this order:
    /// <list type="number">
    /// <item><see cref="RequestBodyClassifier"/>: 413/400 for an oversized or unparseable body.</item>
    /// <item><see cref="UpstreamUnavailableClassifier"/>: 502.</item>
    /// <item><see cref="ConcurrencyClassifier"/>: 409. BEFORE the transient classifier (a lost update is a
    ///   <c>DbUpdateException</c> too, and "retry" would overwrite the other user's edit).</item>
    /// <item><see cref="TransientFaultClassifier"/>: 503 with <c>Retry-After</c>.</item>
    /// </list>
    /// <see cref="Map{TException}(Func{TException, Failure})"/> appends after them; insert into this list to place a
    /// classifier earlier. An exception nobody claims is answered 500 <see cref="ErrorCodes.InternalError"/> at Error.
    /// </summary>
    public IList<IFailureClassifier> Classifiers { get; } = new List<IFailureClassifier>
    {
        new RequestBodyClassifier(),
        new UpstreamUnavailableClassifier(),
        new ConcurrencyClassifier(),
        new TransientFaultClassifier(),
    };

    /// <summary>
    /// Extension members a host allows on an error body besides <c>code</c>, <c>correlationId</c>,
    /// <c>referenceCode</c> and <c>errors</c>. Every other member is removed by the <see cref="ClientErrorScrubber"/>.
    /// </summary>
    public ISet<string> AllowedExtensionMembers { get; } = new HashSet<string>(StringComparer.Ordinal);

    /// <summary>Appends a classifier for <typeparamref name="TException"/> whose failure is built from the exception.</summary>
    public CodelabErrorsOptions Map<TException>(Func<TException, Failure> describe)
        where TException : Exception
    {
        Classifiers.Add(new ExceptionTypeClassifier<TException>(describe));
        return this;
    }

    /// <summary>
    /// Appends a classifier for <typeparamref name="TException"/> answering <paramref name="status"/> with
    /// <paramref name="code"/>, the status's table sentence (<see cref="FrameworkStatusCodes.Describe"/>) and
    /// <paramref name="level"/>.
    /// </summary>
    public CodelabErrorsOptions Map<TException>(int status, string code, LogLevel level)
        where TException : Exception
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(code);

        var failure = new Failure(status, code, FrameworkStatusCodes.Describe(status).Detail, level);
        return Map<TException>(_ => failure);
    }
}
