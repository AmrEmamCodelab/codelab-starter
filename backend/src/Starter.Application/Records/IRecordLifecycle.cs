using Starter.Domain.Records;

namespace Starter.Application.Records;

/// <summary>
/// The lifecycle of any record, implemented by the Records module and called by the module that owns the record type
/// (which keeps its own fields in its own tables, keyed by the record id). Every call acts as the current user and
/// checks ownership, permissions, the workflow catalog and the required-attachments interlock itself; the caller adds
/// only its own field checks (<see cref="IRecordSubmitValidator"/>).
/// </summary>
/// <remarks>
/// After a committed transition the implementation invokes every <see cref="IRecordTransitionObserver"/> in the same
/// request. A refusal is a value (<see cref="SubmitResult"/>, <see cref="TransitionResult"/>); only faults throw.
/// </remarks>
public interface IRecordLifecycle
{
    /// <summary>Creates a draft of <paramref name="recordType"/> owned by the current user (no number yet).</summary>
    /// <exception cref="ArgumentException">The record type key is malformed or not registered.</exception>
    Task<RecordRef> CreateDraftAsync(string recordType, CancellationToken cancellationToken);

    /// <summary>
    /// Submits the caller's draft: ownership, status, every <see cref="IRecordSubmitValidator"/> of the record type, then
    /// the required-attachments interlock (<c>RequiredAttachmentsGate</c>), then allocates the number.
    /// </summary>
    Task<SubmitResult> SubmitAsync(RecordRef record, CancellationToken cancellationToken);

    /// <summary>
    /// Applies workflow action <paramref name="actionKey"/> to the record as the current user, with an optional
    /// <paramref name="reason"/> (required when the action's rule says so).
    /// </summary>
    Task<TransitionResult> TransitionAsync(RecordRef record, string actionKey, string? reason, CancellationToken cancellationToken);

    /// <summary>Cancels the caller's record (the record type's cancel action) with an optional reason.</summary>
    Task<TransitionResult> CancelAsync(RecordRef record, string? reason, CancellationToken cancellationToken);

    /// <summary>Resubmits the caller's returned record after the same checks as <see cref="SubmitAsync"/>; the number is kept.</summary>
    Task<SubmitResult> ResubmitAsync(RecordRef record, CancellationToken cancellationToken);
}
