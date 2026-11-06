using Starter.Domain.Records;

namespace Starter.Modules.Assignments.Domain;

/// <summary>The Assignments module's limits (validators, queries and the column sizes use the same constants).</summary>
public static class AssignmentsLimits
{
    /// <summary>The longest record type key a row stores (the kernel <see cref="RecordTypeKeyFormat.MaxLength"/>).</summary>
    public const int RecordTypeMaxLength = RecordTypeKeyFormat.MaxLength;

    /// <summary>The longest number search text a queue request may send.</summary>
    public const int NumberSearchMaxLength = 32;

    /// <summary>
    /// The most directory pages the unassigned view reads to fill one page (each of
    /// <c>RecordsLimits.MaxPageSize</c> records). It bounds the work of a deep page of a very large queue; a page past
    /// the bound comes back short, never with a wrong record.
    /// </summary>
    public const int UnassignedScanMaxPages = 50;
}
