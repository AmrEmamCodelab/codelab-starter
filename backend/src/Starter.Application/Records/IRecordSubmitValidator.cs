namespace Starter.Application.Records;

/// <summary>
/// The owning module's field check before a record of <see cref="RecordType"/> is submitted or resubmitted (the server
/// half of the wizard contract). Optional: a record type without one is submitted on the kernel checks alone. Several
/// may exist per record type; the Records module runs them all and returns every field error at once.
/// </summary>
public interface IRecordSubmitValidator
{
    /// <summary>The record type this validator checks (<c>RecordTypeKeyFormat</c>).</summary>
    string RecordType { get; }

    /// <summary>
    /// Every field error of record <paramref name="recordId"/> (camelCase field names); empty when it may be submitted.
    /// Never throws for invalid data; reads the record's own fields from the owning module's tables.
    /// </summary>
    Task<IReadOnlyList<FieldError>> ValidateForSubmitAsync(long recordId, CancellationToken cancellationToken);
}
