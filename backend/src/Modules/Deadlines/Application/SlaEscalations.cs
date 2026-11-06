using System.Runtime.ExceptionServices;
using Microsoft.Extensions.Logging;
using Starter.Application.Records;
using Starter.Modules.Deadlines.Domain;

namespace Starter.Modules.Deadlines.Application;

/// <summary>A marked breach with its record (null when the record is gone) and the users to notify.</summary>
/// <param name="Deadline">The breached deadline (tracked).</param>
/// <param name="Record">The record's summary, or null.</param>
/// <param name="Recipients">The users to notify (may be empty).</param>
public sealed record Breach(RecordDeadline Deadline, RecordSummary? Record, IReadOnlyList<long> Recipients);

/// <summary>
/// Loads the records and the recipients of breaches in one directory call and one recipients resolution. The record
/// directory is the Records module's; a host without it (no record type at all) still starts, and
/// <see cref="CanRead"/> is false.
/// </summary>
public sealed class BreachLoader(IEnumerable<IRecordDirectory> directories, EscalationRecipients recipients)
{
    private readonly IRecordDirectory? _records = directories.FirstOrDefault();

    /// <summary>True when a record directory is registered.</summary>
    public bool CanRead => _records is not null;

    /// <summary>The breaches of <paramref name="deadlines"/>, in the same order.</summary>
    /// <exception cref="InvalidOperationException">No record directory is registered (check <see cref="CanRead"/> first).</exception>
    public async Task<IReadOnlyList<Breach>> LoadAsync(IReadOnlyList<RecordDeadline> deadlines, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(deadlines);
        if (deadlines.Count == 0)
        {
            return [];
        }

        var records = _records ?? throw new InvalidOperationException("No record directory is registered.");
        var summaries = (await records.GetAsync([.. deadlines.Select(deadline => deadline.RecordId).Distinct()], cancellationToken))
            .ToDictionary(summary => summary.Id);
        var audience = await recipients.ResolveAsync([.. summaries.Values], cancellationToken);
        return [.. deadlines.Select(deadline => new Breach(
            deadline,
            summaries.GetValueOrDefault(deadline.RecordId),
            audience.GetValueOrDefault(deadline.RecordId, [])))];
    }
}

/// <summary>What an escalation batch did; <see cref="FirstFailure"/> is rethrown by the scan after the batch.</summary>
public sealed class EscalationTally
{
    /// <summary>Breaches escalated to at least one recipient.</summary>
    public int Escalated { get; set; }

    /// <summary>Breaches with nobody to notify.</summary>
    public int Unaddressed { get; set; }

    /// <summary>Breaches whose SLA definition no longer exists.</summary>
    public int Skipped { get; set; }

    /// <summary>Stale deadlines closed.</summary>
    public int Closed { get; set; }

    /// <summary>The first escalation failure, or null.</summary>
    public ExceptionDispatchInfo? FirstFailure { get; set; }
}

/// <summary>
/// Raises ONE escalation notification per marked breach (one batch of <see cref="DeadlinesLimits.ScanBatchSize"/> per
/// run). Each escalation is claimed (<see cref="RecordDeadline.EscalatedAt"/> saved) before it is sent, so a re-run never
/// sends it again. A deadline whose record is gone, terminal or in another status is closed instead (an observer that
/// failed after a transition heals here).
/// </summary>
/// <remarks>
/// A failed escalation that reached no recipient is given back for the next run; one that reached some recipients stays
/// claimed (at most once per recipient). Each breach is isolated: the batch goes on, and the first failure is kept in
/// <see cref="EscalationTally.FirstFailure"/>.
/// </remarks>
public sealed partial class SlaEscalations(
    IRecordDeadlineStore deadlines,
    SlaPolicyCatalog policies,
    BreachLoader breaches,
    SlaEscalationNotifier notifier,
    ILogger<SlaEscalations> logger)
{
    /// <summary>Escalates the waiting breaches at <paramref name="now"/>.</summary>
    public async Task<EscalationTally> EscalateAsync(DateTimeOffset now, CancellationToken cancellationToken)
    {
        var tally = new EscalationTally();
        if (!breaches.CanRead)
        {
            LogNoDirectory(logger);
            return tally;
        }

        var waiting = await deadlines.AwaitingEscalationAsync(DeadlinesLimits.ScanBatchSize, cancellationToken);
        foreach (var breach in await breaches.LoadAsync(waiting, cancellationToken))
        {
            await EscalateOneAsync(breach, now, tally, cancellationToken);
        }

        return tally;
    }

    private async Task EscalateOneAsync(Breach breach, DateTimeOffset now, EscalationTally tally, CancellationToken cancellationToken)
    {
        var deadline = breach.Deadline;
        if (breach.Record is not { } record || record.Status != deadline.Status || record.IsTerminal)
        {
            deadline.Close(now);
            await deadlines.SaveChangesAsync(cancellationToken);
            tally.Closed++;
            LogStaleClosed(logger, deadline.Id, deadline.RecordId);
            return;
        }

        deadline.ClaimEscalation(now);
        await deadlines.SaveChangesAsync(cancellationToken);
        if (policies.Find(deadline.RecordType, deadline.Status) is not { } definition)
        {
            tally.Skipped++;
            LogPolicyMissing(logger, deadline.Id, deadline.Status);
            return;
        }

        if (breach.Recipients.Count == 0)
        {
            tally.Unaddressed++;
            LogNoRecipient(logger, deadline.Id, deadline.RecordId);
            return;
        }

        await SendAsync(definition, breach, tally, cancellationToken);
    }

    private async Task SendAsync(SlaDefinition definition, Breach breach, EscalationTally tally, CancellationToken cancellationToken)
    {
        var sent = 0;
        try
        {
            foreach (var userId in breach.Recipients)
            {
                await notifier.NotifyAsync(definition, breach.Deadline, breach.Record!, userId, cancellationToken);
                sent++;
            }

            tally.Escalated++;
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            // Load-bearing: isolates one breach (the batch goes on); the scan rethrows the first failure afterwards.
            if (sent == 0)
            {
                breach.Deadline.ReleaseEscalation();
                await deadlines.SaveChangesAsync(cancellationToken);
            }

            tally.FirstFailure ??= ExceptionDispatchInfo.Capture(exception);
            LogEscalationFailed(logger, exception, breach.Deadline.Id, sent);
        }
    }

    [LoggerMessage(EventId = 1001, Level = LogLevel.Information, Message = "SLA deadline {DeadlineId} of record {RecordId} closed: the record is gone, terminal or left the status")]
    private static partial void LogStaleClosed(ILogger logger, long deadlineId, long recordId);

    [LoggerMessage(EventId = 1002, Level = LogLevel.Warning, Message = "SLA deadline {DeadlineId} breached in status {Status}, which no longer has an SLA definition: no escalation sent")]
    private static partial void LogPolicyMissing(ILogger logger, long deadlineId, int status);

    [LoggerMessage(EventId = 1003, Level = LogLevel.Warning, Message = "SLA deadline {DeadlineId} of record {RecordId} breached with no assignee and no queue staff: no escalation sent")]
    private static partial void LogNoRecipient(ILogger logger, long deadlineId, long recordId);

    [LoggerMessage(EventId = 1004, Level = LogLevel.Error, Message = "SLA escalation of deadline {DeadlineId} failed after {Sent} recipients")]
    private static partial void LogEscalationFailed(ILogger logger, Exception exception, long deadlineId, int sent);

    [LoggerMessage(EventId = 1008, Level = LogLevel.Warning, Message = "SLA escalation skipped: no record directory is registered (the Records module is not in the host)")]
    private static partial void LogNoDirectory(ILogger logger);
}
