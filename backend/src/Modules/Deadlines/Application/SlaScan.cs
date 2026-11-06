using Starter.Modules.Deadlines.Domain;

namespace Starter.Modules.Deadlines.Application;

/// <summary>What one scan pass did.</summary>
/// <param name="Breached">Deadlines newly marked breached.</param>
/// <param name="Escalated">Breaches escalated to at least one recipient.</param>
/// <param name="Unaddressed">Breaches escalated to nobody (no assignee and no queue staff could be named).</param>
/// <param name="Skipped">Breaches whose SLA definition no longer exists (claimed without a notification).</param>
/// <param name="Closed">Stale deadlines closed because their record is gone, terminal or no longer in the status.</param>
public sealed record SlaScanResult(int Breached, int Escalated, int Unaddressed, int Skipped, int Closed);

/// <summary>
/// One pass of the SLA timer at the current <see cref="TimeProvider"/> time: marks every open deadline whose
/// <see cref="RecordDeadline.DueAt"/> has passed as breached (in batches of <see cref="DeadlinesLimits.ScanBatchSize"/>
/// until none is left), then escalates the waiting breaches (<see cref="SlaEscalations"/>). Safe to run again from the
/// start: a marked breach is never marked again, and an escalation is claimed before it is sent.
/// </summary>
public sealed class SlaScan(IRecordDeadlineStore deadlines, SlaEscalations escalations, TimeProvider time)
{
    /// <summary>Runs one pass.</summary>
    /// <exception cref="Exception">The first escalation that failed (rethrown after every other breach was handled).</exception>
    public async Task<SlaScanResult> RunAsync(CancellationToken cancellationToken)
    {
        var now = time.GetUtcNow();
        var breached = await MarkBreachesAsync(now, cancellationToken);
        var tally = await escalations.EscalateAsync(now, cancellationToken);
        tally.FirstFailure?.Throw();
        return new SlaScanResult(breached, tally.Escalated, tally.Unaddressed, tally.Skipped, tally.Closed);
    }

    private async Task<int> MarkBreachesAsync(DateTimeOffset now, CancellationToken cancellationToken)
    {
        var marked = 0;
        while (true)
        {
            var due = await deadlines.DueForBreachAsync(now, DeadlinesLimits.ScanBatchSize, cancellationToken);
            var batch = due.Count(deadline => deadline.MarkBreached(now));
            if (batch == 0)
            {
                return marked;
            }

            await deadlines.SaveChangesAsync(cancellationToken);
            marked += batch;
        }
    }
}
