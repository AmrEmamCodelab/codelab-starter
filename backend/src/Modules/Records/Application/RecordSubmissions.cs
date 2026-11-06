using Starter.Application.Attachments;
using Starter.Application.Numbering;
using Starter.Application.Records;
using Starter.Application.Results;
using Starter.Application.Workflow;

namespace Starter.Modules.Records.Application;

/// <summary>
/// How a submit or resubmit ended, before it is shaped for the kernel port (<see cref="ToSubmitResult"/>) or the generic
/// transitions endpoint (<see cref="ToTransitionResult"/>): each keeps its own error codes.
/// </summary>
public abstract record SubmitAttempt
{
    private SubmitAttempt()
    {
    }

    /// <summary>The result the <see cref="IRecordLifecycle"/> port returns.</summary>
    public abstract SubmitResult ToSubmitResult();

    /// <summary>The result the generic transitions endpoint returns (workflow refusals keep their WORKFLOW_* codes).</summary>
    public abstract TransitionResult ToTransitionResult();

    /// <summary>Submitted: the record's number and new status.</summary>
    public sealed record Submitted(string Number, int NewStatus) : SubmitAttempt
    {
        /// <inheritdoc />
        public override SubmitResult ToSubmitResult() => new SubmitResult.Submitted(Number);

        /// <inheritdoc />
        public override TransitionResult ToTransitionResult() => new TransitionResult.Transitioned(NewStatus);
    }

    /// <summary>The caller or the record failed the loader (403 unknown actor, 404 missing or hidden record).</summary>
    public sealed record LoadRefused(Refusal Refusal) : SubmitAttempt
    {
        /// <inheritdoc />
        public override SubmitResult ToSubmitResult() =>
            SubmitResult.Refuse(Refusal.Category == RefusalCategory.NotFound ? SubmitRefusal.NotFound : SubmitRefusal.NotOwner);

        /// <inheritdoc />
        public override TransitionResult ToTransitionResult() => TransitionResult.Refuse(Refusal);
    }

    /// <summary>The workflow refused the action (wrong status, not the caller's action).</summary>
    public sealed record WorkflowRefused(TransitionDecision.Refused Decision) : SubmitAttempt
    {
        /// <inheritdoc />
        public override SubmitResult ToSubmitResult() =>
            SubmitResult.Refuse(Decision.Refusal == WorkflowRefusal.Forbidden ? SubmitRefusal.NotOwner : SubmitRefusal.NotSubmittable);

        /// <inheritdoc />
        public override TransitionResult ToTransitionResult() => TransitionResult.Refuse(WorkflowRefusals.ToRefusal(Decision));
    }

    /// <summary>A submit check refused it (validation, required attachments, a concurrent change).</summary>
    public sealed record SubmitRefused(SubmitResult.Refused Result) : SubmitAttempt
    {
        /// <inheritdoc />
        public override SubmitResult ToSubmitResult() => Result;

        /// <inheritdoc />
        public override TransitionResult ToTransitionResult() => TransitionResult.Refuse(Result.ToRefusal());
    }
}

/// <summary>
/// Submit and resubmit, in this order: the loader (caller, type, visibility), the workflow decision for the
/// <see cref="RecordLifecycleActions.Submit"/> or <see cref="RecordLifecycleActions.Resubmit"/> rule, every registered
/// <see cref="IRecordSubmitValidator"/> of the record type, the required-attachments interlock
/// (<see cref="RequiredAttachmentsGate.RefuseWhileMissingAsync"/>), then the number (taken only now, so a refused submit
/// burns none; a resubmit keeps the first number) and the commit.
/// </summary>
public sealed class RecordSubmissions(
    RecordLoader loader,
    RecordCommits commits,
    IEnumerable<IRecordSubmitValidator> validators,
    IEnumerable<IRequiredAttachments> interlocks,
    IRequestNumberAllocator numbers)
{
    /// <summary>Submits (or resubmits, per <paramref name="actionKey"/>) record <paramref name="recordId"/> of <paramref name="recordType"/>.</summary>
    public async Task<SubmitAttempt> AttemptAsync(string? recordType, long recordId, string actionKey, CancellationToken cancellationToken)
    {
        var load = await loader.LoadAsync(recordType, recordId, cancellationToken);
        if (!load.IsSuccess)
        {
            return new SubmitAttempt.LoadRefused(load.Refusal);
        }

        var loaded = load.Value;
        var decision = RecordWorkflowEngine.Decide(loaded.Type.Catalog, loaded.Record.Status, actionKey, reason: null, loaded.Standing.ForWorkflow());
        if (decision is TransitionDecision.Refused refused)
        {
            return new SubmitAttempt.WorkflowRefused(refused);
        }

        var check = await CheckAsync(loaded, cancellationToken);
        if (check is not null)
        {
            return new SubmitAttempt.SubmitRefused(check);
        }

        var number = loaded.Record.Number ?? await numbers.NextAsync(loaded.Type.Registration.NumberPrefix, cancellationToken);
        var committed = await commits.CommitAsync(loaded, (TransitionDecision.Allowed)decision, number, cancellationToken);
        return committed.IsSuccess
            ? new SubmitAttempt.Submitted(number, committed.Value)
            : new SubmitAttempt.SubmitRefused((SubmitResult.Refused)SubmitResult.Refuse(SubmitRefusal.Concurrency));
    }

    private async Task<SubmitResult.Refused?> CheckAsync(LoadedRecord loaded, CancellationToken cancellationToken)
    {
        var errors = new List<FieldError>();
        foreach (var validator in validators.Where(validator => string.Equals(validator.RecordType, loaded.Type.RecordType, StringComparison.Ordinal)))
        {
            errors.AddRange(await validator.ValidateForSubmitAsync(loaded.Record.Id, cancellationToken));
        }

        if (errors.Count > 0)
        {
            return (SubmitResult.Refused)SubmitResult.Invalid(errors);
        }

        foreach (var interlock in interlocks)
        {
            var missing = await interlock.RefuseWhileMissingAsync(loaded.Type.Registration.AttachableRecordType, loaded.Record.Id, cancellationToken);
            if (missing is not null)
            {
                return (SubmitResult.Refused)SubmitResult.Refuse(SubmitRefusal.AttachmentsMissing);
            }
        }

        return null;
    }
}
