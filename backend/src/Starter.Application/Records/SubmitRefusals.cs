using Starter.Application.Attachments;
using Starter.Application.Results;

namespace Starter.Application.Records;

/// <summary>
/// The one mapping of a <see cref="SubmitRefusal"/> to the kernel <see cref="Refusal"/> (and so to the HTTP status and
/// error code). Every submit and resubmit of every record type goes through it; no controller switches.
/// </summary>
/// <remarks>
/// NotFound and NotOwner: NotFound (404) <see cref="RecordsErrorCodes.NotFound"/>. NotSubmittable: Conflict (409)
/// <see cref="RecordsErrorCodes.NotSubmittable"/>. ValidationFailed: Invalid (400) <see cref="PlatformErrorCodes.Validation"/>
/// with the field errors. AttachmentsMissing: Conflict (409) <see cref="AttachmentErrorCodes.RequiredMissing"/>.
/// Concurrency: Conflict (409) <see cref="RecordsErrorCodes.Concurrency"/>.
/// </remarks>
public static class SubmitRefusals
{
    /// <summary>The kernel refusal of <paramref name="refusal"/>.</summary>
    /// <param name="refusal">Why the submit was refused.</param>
    /// <param name="fieldErrors">The field errors: one or more for <see cref="SubmitRefusal.ValidationFailed"/>, otherwise null or empty.</param>
    /// <exception cref="ArgumentException">The field errors do not fit the refusal (<see cref="EnsureConsistent"/>).</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="refusal"/> is not a defined member.</exception>
    public static Refusal ToRefusal(SubmitRefusal refusal, IReadOnlyList<FieldError>? fieldErrors)
    {
        EnsureConsistent(refusal, fieldErrors);
        return refusal switch
        {
            SubmitRefusal.NotFound or SubmitRefusal.NotOwner => Refusal.NotFound(RecordsErrorCodes.NotFound),
            SubmitRefusal.NotSubmittable => Refusal.Conflict(RecordsErrorCodes.NotSubmittable),
            SubmitRefusal.ValidationFailed => Refusal.Invalid(ToDictionary(fieldErrors!), PlatformErrorCodes.Validation),
            SubmitRefusal.AttachmentsMissing => Refusal.Conflict(AttachmentErrorCodes.RequiredMissing),
            SubmitRefusal.Concurrency => Refusal.Conflict(RecordsErrorCodes.Concurrency),
            _ => throw UndefinedRefusal(refusal),
        };
    }

    /// <summary>
    /// Refuses field errors that do not fit <paramref name="refusal"/>: <see cref="SubmitRefusal.ValidationFailed"/>
    /// carries at least one, every other refusal carries none. Called by <see cref="ToRefusal"/> and by
    /// <see cref="SubmitResult.Refused"/>, so an inconsistent refusal fails where it is built.
    /// </summary>
    /// <exception cref="ArgumentException">The field errors do not fit the refusal.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="refusal"/> is not a defined member.</exception>
    public static void EnsureConsistent(SubmitRefusal refusal, IReadOnlyList<FieldError>? fieldErrors)
    {
        if (!Enum.IsDefined(refusal))
        {
            throw UndefinedRefusal(refusal);
        }

        var isValidation = refusal == SubmitRefusal.ValidationFailed;
        var hasFieldErrors = fieldErrors is { Count: > 0 };
        if (isValidation != hasFieldErrors)
        {
            throw new ArgumentException(
                isValidation ? "A validation refusal carries at least one field error." : "Only a validation refusal carries field errors.",
                nameof(fieldErrors));
        }
    }

    /// <summary>The field errors grouped by field (codes in the order given, field names compared ordinally).</summary>
    private static Dictionary<string, string[]> ToDictionary(IReadOnlyList<FieldError> fieldErrors) =>
        fieldErrors
            .GroupBy(error => error.Field, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.Select(error => error.Code).ToArray(), StringComparer.Ordinal);

    private static ArgumentOutOfRangeException UndefinedRefusal(SubmitRefusal refusal) =>
        new(nameof(refusal), (int)refusal, "Not a defined submit refusal.");
}
