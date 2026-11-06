using Starter.Domain.Records;

namespace Starter.Modules.Deadlines.Domain;

/// <summary>The Deadlines module's sizes (the column sizes and the scan batches use the same constants).</summary>
public static class DeadlinesLimits
{
    /// <summary>The longest record type key (the record type column).</summary>
    public const int RecordTypeMaxLength = RecordTypeKeyFormat.MaxLength;

    /// <summary>
    /// The most deadlines one scan pass marks or escalates per batch. Marking repeats batches until none is left; the
    /// escalation handles one batch per run (a larger backlog is escalated by the next ticks, five minutes apart).
    /// </summary>
    public const int ScanBatchSize = 100;
}
