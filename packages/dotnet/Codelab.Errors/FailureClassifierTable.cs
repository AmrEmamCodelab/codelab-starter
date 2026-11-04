using Codelab.Observability;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Codelab.Errors;

/// <summary>A classification and the name of the classifier that made it (logged, so a misrouted exception is obvious).</summary>
/// <param name="Failure">How the failure is reported.</param>
/// <param name="Classifier">The claiming classifier's type name, or <see cref="FailureClassifierTable.UnclassifiedName"/>.</param>
public readonly record struct Classification(Failure Failure, string Classifier);

/// <summary>
/// The ordered classifier table (<see cref="CodelabErrorsOptions.Classifiers"/>), shared by the HTTP handler and,
/// through <see cref="IFailureCodeResolver"/>, by the job failure log filter, so a failure has the same code in a job
/// log as on the wire. An exception nobody claims is <see cref="Unclassified"/>: silence about the cause is the default.
/// </summary>
public sealed class FailureClassifierTable : IFailureCodeResolver
{
    /// <summary>The classifier name of an exception nobody claimed: the bug class an on-call is paged for.</summary>
    public const string UnclassifiedName = "Unclassified";

    /// <summary>The fallback failure: 500 <see cref="ErrorCodes.InternalError"/> at Error.</summary>
    public static readonly Failure Unclassified = new(
        StatusCodes.Status500InternalServerError,
        ErrorCodes.InternalError,
        "An unexpected error occurred. Quote the reference code when reporting this.",
        LogLevel.Error);

    private readonly IFailureClassifier[] _classifiers;

    /// <summary>The table configured by <c>AddCodelabErrors</c>.</summary>
    public FailureClassifierTable(IOptions<CodelabErrorsOptions> options)
    {
        ArgumentNullException.ThrowIfNull(options);
        _classifiers = [.. options.Value.Classifiers];
    }

    /// <summary>The first claim in table order, or <see cref="Unclassified"/>.</summary>
    public Classification Classify(Exception exception, HttpContext? context)
    {
        ArgumentNullException.ThrowIfNull(exception);

        foreach (var classifier in _classifiers)
        {
            if (classifier.Classify(exception, context) is { } failure)
            {
                return new Classification(failure, classifier.GetType().Name);
            }
        }

        return new Classification(Unclassified, UnclassifiedName);
    }

    /// <inheritdoc />
    public FailureCode Resolve(Exception? exception)
    {
        var failure = exception is null ? Unclassified : Classify(exception, context: null).Failure;
        return new FailureCode(failure.Code, failure.Level);
    }
}
