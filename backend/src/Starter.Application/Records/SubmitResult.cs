using Starter.Application.Results;

namespace Starter.Application.Records;

/// <summary>
/// The result of <see cref="IRecordLifecycle.SubmitAsync"/> and <see cref="IRecordLifecycle.ResubmitAsync"/>: the
/// record's reference number (<see cref="Submitted"/>), or why it was refused (<see cref="Refused"/>). Closed: no
/// other subtype exists.
/// </summary>
public abstract record SubmitResult
{
    private SubmitResult()
    {
    }

    /// <summary>The result as a use-case outcome: the number, or the refusal from the one central mapping.</summary>
    public abstract Outcome<string> ToOutcome();

    /// <summary>A refusal without field errors (any reason but <see cref="SubmitRefusal.ValidationFailed"/>).</summary>
    /// <exception cref="ArgumentException">The reason is <see cref="SubmitRefusal.ValidationFailed"/> (use <see cref="Invalid"/>).</exception>
    public static SubmitResult Refuse(SubmitRefusal reason) => new Refused(reason, FieldErrors: null);

    /// <summary>A validation refusal carrying every field error.</summary>
    /// <exception cref="ArgumentException">No field error is given.</exception>
    public static SubmitResult Invalid(IReadOnlyList<FieldError> fieldErrors) => new Refused(SubmitRefusal.ValidationFailed, fieldErrors);

    /// <summary>The record was submitted (or resubmitted) and carries <paramref name="Number"/>.</summary>
    /// <param name="Number">The <c>PREFIX-YYYY-NNN</c> reference number (kept on resubmit).</param>
    /// <exception cref="ArgumentException">The number is blank.</exception>
    public sealed record Submitted(string Number) : SubmitResult
    {
        /// <summary>The reference number.</summary>
        public string Number { get; } = string.IsNullOrWhiteSpace(Number)
            ? throw new ArgumentException("A submitted record has a reference number.", nameof(Number))
            : Number;

        /// <inheritdoc/>
        public override Outcome<string> ToOutcome() => Outcome.Success(Number);
    }

    /// <summary>The submit was refused for <paramref name="Reason"/>.</summary>
    /// <param name="Reason">Why.</param>
    /// <param name="FieldErrors">Every field error for <see cref="SubmitRefusal.ValidationFailed"/>; otherwise null.</param>
    /// <exception cref="ArgumentException">The field errors do not fit the reason (<see cref="SubmitRefusals.EnsureConsistent"/>).</exception>
    public sealed record Refused(SubmitRefusal Reason, IReadOnlyList<FieldError>? FieldErrors) : SubmitResult
    {
        /// <summary>Why the submit was refused.</summary>
        public SubmitRefusal Reason { get; } = Checked(Reason, FieldErrors);

        /// <summary>The kernel refusal (<see cref="SubmitRefusals.ToRefusal"/>).</summary>
        public Refusal ToRefusal() => SubmitRefusals.ToRefusal(Reason, FieldErrors);

        /// <inheritdoc/>
        public override Outcome<string> ToOutcome() => ToRefusal();

        private static SubmitRefusal Checked(SubmitRefusal reason, IReadOnlyList<FieldError>? fieldErrors)
        {
            SubmitRefusals.EnsureConsistent(reason, fieldErrors);
            return reason;
        }
    }
}
