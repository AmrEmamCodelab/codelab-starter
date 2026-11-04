using Microsoft.AspNetCore.Http;

namespace Codelab.Errors.Classifiers;

/// <summary>
/// Claims <typeparamref name="TException"/> (and its subclasses) with a failure built from the exception. This is how
/// a host maps its own domain exceptions without the package knowing them: Starter.Domain defines
/// <c>BusinessRuleException</c>, the host registers <c>options.Map&lt;BusinessRuleException&gt;(...)</c>
/// (<see cref="CodelabErrorsOptions.Map{TException}(Func{TException, Failure})"/>).
/// </summary>
/// <typeparam name="TException">The exception type claimed.</typeparam>
public sealed class ExceptionTypeClassifier<TException>(Func<TException, Failure> describe) : IFailureClassifier
    where TException : Exception
{
    private readonly Func<TException, Failure> _describe = describe ?? throw new ArgumentNullException(nameof(describe));

    /// <inheritdoc />
    public Failure? Classify(Exception exception, HttpContext? context) =>
        exception is TException claimed ? _describe(claimed) : null;
}
