namespace Starter.Domain.Workflow;

/// <summary>One problem in a record type's catalog, worded so a reviewer can act on it.</summary>
/// <param name="RecordType">The catalog's record type.</param>
/// <param name="Kind">What is wrong.</param>
/// <param name="Subject">The offending status or rule (<c>status under-review (3)</c>, <c>approve (3 -> 6)</c>).</param>
/// <param name="Message">What is wrong and how to fix it.</param>
public sealed record CatalogViolation(string RecordType, CatalogViolationKind Kind, string Subject, string Message)
{
    /// <inheritdoc />
    public override string ToString() => $"[{RecordType}] {Kind} {Subject}: {Message}";
}
