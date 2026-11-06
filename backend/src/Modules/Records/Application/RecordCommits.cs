using Microsoft.Extensions.Logging;
using Starter.Application.Records;
using Starter.Application.Results;
using Starter.Application.Workflow;
using Starter.Domain.Auditing;

namespace Starter.Modules.Records.Application;

/// <summary>
/// Applies an allowed transition: the new status (and the number of a first submit), one timeline entry with the actor,
/// side, from/to status and reason, saved together in ONE transaction; then the side effects
/// (<see cref="TransitionEffects"/>). A concurrent change of the record makes the save refuse with 409
/// <see cref="RecordsErrorCodes.Concurrency"/> and nothing is written.
/// </summary>
public sealed class RecordCommits(IRecordStore store, TimeProvider clock, TransitionEffects effects, ILogger<RecordCommits> logger)
{
    /// <summary>Saves <paramref name="allowed"/> on <paramref name="loaded"/>; the new status, or the concurrency refusal.</summary>
    /// <param name="loaded">The record and its caller.</param>
    /// <param name="allowed">The engine's decision.</param>
    /// <param name="number">The reference number a submit took, or null.</param>
    /// <param name="cancellationToken">Cancels the save.</param>
    public async Task<Outcome<int>> CommitAsync(LoadedRecord loaded, TransitionDecision.Allowed allowed, string? number, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(loaded);
        ArgumentNullException.ThrowIfNull(allowed);
        var record = loaded.Record;
        var rule = allowed.Rule;
        var actor = loaded.Standing.Caller;
        var fromStatus = record.Status;
        var now = clock.GetUtcNow();
        if (number is not null)
        {
            record.MarkSubmitted(number, now);
        }

        record.MoveTo(rule.ToStatus, now);
        store.Add(new Domain.RecordHistory(
            new RecordHistoryFacts(
                RecordId: record.Id,
                Action: rule.ActionKey,
                FromStatus: fromStatus,
                ToStatus: rule.ToStatus,
                Reason: allowed.Reason,
                ActorUserId: actor.UserId,
                ActorName: actor.DisplayName,
                At: now),
            rule.Side));
        if (!await store.TrySaveAsync(cancellationToken))
        {
            return Refusal.Conflict(RecordsErrorCodes.Concurrency);
        }

        RecordsLog.Transitioned(logger, record.Id, record.RecordType, fromStatus, rule.ToStatus, rule.ActionKey, actor.UserId);
        await effects.AfterCommitAsync(
            new CommittedTransition(record, rule, fromStatus, allowed.Reason, actor, loaded.Type, now),
            cancellationToken);
        return rule.ToStatus;
    }
}
