using Starter.Application.Validation;
using Starter.Modules.Assignments.Domain;

namespace Starter.Modules.Assignments.Application.Queues;

/// <summary>The wire words of <see cref="QueueView"/> (<c>view=unassigned|mine|all</c>), compared ordinally ignoring case.</summary>
public static class QueueViews
{
    /// <summary>The word of <see cref="QueueView.Unassigned"/>.</summary>
    public const string Unassigned = "unassigned";

    /// <summary>The word of <see cref="QueueView.Mine"/>.</summary>
    public const string Mine = "mine";

    /// <summary>The word of <see cref="QueueView.All"/>.</summary>
    public const string All = "all";

    /// <summary>The view a word names, or null for a missing or unknown word.</summary>
    public static QueueView? Parse(string? word) => word?.Trim().ToLowerInvariant() switch
    {
        Unassigned => QueueView.Unassigned,
        Mine => QueueView.Mine,
        All => QueueView.All,
        _ => null,
    };
}

/// <summary>
/// One work queue list request (<c>GET api/queues/{recordType}</c>), as the query string sent it. Validated by
/// <see cref="WorkQueueRequestValidator"/>; the status is checked against the queue by <see cref="GetWorkQueuePage"/>.
/// </summary>
/// <param name="RecordType">The record type key from the route.</param>
/// <param name="View">The view word (<see cref="QueueViews"/>); required.</param>
/// <param name="Status">Only this status (one of the queue's open statuses), or null for all of them.</param>
/// <param name="Number">Only numbers containing this text, or null/blank.</param>
/// <param name="SubmittedFrom">Only records first submitted at or after this instant, or null.</param>
/// <param name="SubmittedTo">Only records first submitted before this instant, or null.</param>
/// <param name="Page">The 1-based page, or null for the first.</param>
/// <param name="PageSize">The page size, or null for the default (capped at the maximum).</param>
public sealed record WorkQueueRequest(
    string RecordType,
    string? View,
    int? Status,
    string? Number,
    DateTimeOffset? SubmittedFrom,
    DateTimeOffset? SubmittedTo,
    int? Page,
    int? PageSize)
{
    /// <summary>The trimmed number search, or null when blank.</summary>
    public string? NumberSearch => string.IsNullOrWhiteSpace(Number) ? null : Number.Trim();
}

/// <summary>Validates <see cref="WorkQueueRequest"/>: every field error at once, never throws.</summary>
public sealed class WorkQueueRequestValidator : IValidator<WorkQueueRequest>
{
    /// <inheritdoc />
    public ValidationErrors Validate(WorkQueueRequest input)
    {
        ArgumentNullException.ThrowIfNull(input);
        var errors = new ValidationErrors();
        if (string.IsNullOrWhiteSpace(input.View))
        {
            errors.Add(FieldNames.Of(nameof(WorkQueueRequest.View)), ValidationMessages.Required);
        }
        else if (QueueViews.Parse(input.View) is null)
        {
            errors.Add(FieldNames.Of(nameof(WorkQueueRequest.View)), ValidationMessages.InvalidFormat);
        }

        if (input.NumberSearch is { Length: > AssignmentsLimits.NumberSearchMaxLength })
        {
            errors.Add(FieldNames.Of(nameof(WorkQueueRequest.Number)), ValidationMessages.TooLong);
        }

        if (input is { SubmittedFrom: { } from, SubmittedTo: { } to } && to < from)
        {
            errors.Add(FieldNames.Of(nameof(WorkQueueRequest.SubmittedTo)), ValidationMessages.InvalidFormat);
        }

        return errors;
    }
}
