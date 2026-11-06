using Starter.Application.Results;

namespace Starter.Application.Records;

/// <summary>
/// The result of <see cref="IRecordLifecycle.TransitionAsync"/> and <see cref="IRecordLifecycle.CancelAsync"/>: the
/// record's new status (<see cref="Transitioned"/>), or the refusal (<see cref="Refused"/>) already carrying its
/// category and code (the workflow error codes or <see cref="RecordsErrorCodes"/>). Closed: no other subtype exists.
/// </summary>
public abstract record TransitionResult
{
    private TransitionResult()
    {
    }

    /// <summary>The result as a use-case outcome: the new status, or the refusal.</summary>
    public abstract Outcome<int> ToOutcome();

    /// <summary>A refused transition.</summary>
    /// <exception cref="ArgumentNullException">The refusal is null.</exception>
    public static TransitionResult Refuse(Refusal refusal) => new Refused(refusal);

    /// <summary>The record moved to <paramref name="NewStatus"/>.</summary>
    /// <param name="NewStatus">The record type's status value after the transition.</param>
    public sealed record Transitioned(int NewStatus) : TransitionResult
    {
        /// <inheritdoc/>
        public override Outcome<int> ToOutcome() => Outcome.Success(NewStatus);
    }

    /// <summary>The transition was refused.</summary>
    /// <param name="Refusal">The refusal, with its category and code.</param>
    /// <exception cref="ArgumentNullException">The refusal is null.</exception>
    public sealed record Refused(Refusal Refusal) : TransitionResult
    {
        /// <summary>The refusal.</summary>
        public Refusal Refusal { get; } = Refusal ?? throw new ArgumentNullException(nameof(Refusal));

        /// <inheritdoc/>
        public override Outcome<int> ToOutcome() => Refusal;
    }
}
