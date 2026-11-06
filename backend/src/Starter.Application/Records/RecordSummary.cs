namespace Starter.Application.Records;

/// <summary>
/// The record-type-neutral facts of one record, as the Records module stores them. Statuses are the record type's own
/// int values; their keys and wording come from the record type's workflow definition.
/// </summary>
/// <param name="Id">The internal record id (encode it as a PublicId before it leaves the API).</param>
/// <param name="RecordType">The record type key.</param>
/// <param name="Number">The <c>PREFIX-YYYY-NNN</c> number, or null while the record is a draft that was never submitted.</param>
/// <param name="Status">The current status value.</param>
/// <param name="OwnerUserId">The requester who owns the record.</param>
/// <param name="OrganizationId">The owner's organization when the record was created, or null (org-scoped queues filter on it).</param>
/// <param name="CreatedAt">When the draft was created.</param>
/// <param name="SubmittedAt">When the record was first submitted, or null.</param>
/// <param name="UpdatedAt">When the record last changed.</param>
/// <param name="IsTerminal">True when the current status is terminal (no action is offered).</param>
public sealed record RecordSummary(
    long Id,
    string RecordType,
    string? Number,
    int Status,
    long OwnerUserId,
    long? OrganizationId,
    DateTimeOffset CreatedAt,
    DateTimeOffset? SubmittedAt,
    DateTimeOffset UpdatedAt,
    bool IsTerminal);
