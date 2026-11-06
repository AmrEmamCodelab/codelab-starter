using System.Globalization;
using Codelab.Security.PublicIds;
using Starter.Application.Auditing;
using Starter.Application.Results;
using Starter.Application.Validation;
using Starter.Domain.Auditing;

namespace Starter.Modules.Audit.Application;

/// <summary>The audit viewer's filter, as sent in the query string. Every value is optional.</summary>
/// <param name="EntityType">The audited entity type name (as the trail lists it), or null for all.</param>
/// <param name="EntityId">One row of <paramref name="EntityType"/> (needs the type), or null for all.</param>
/// <param name="Before">Only entries older than this one (the cursor: the last id of the previous page), or null.</param>
/// <param name="Take">The page size, 1 to <see cref="AuditPaging.MaxTake"/>; null is <see cref="AuditPaging.DefaultTake"/>.</param>
public sealed record AuditTrailListQuery(string? EntityType, PublicId<AuditedEntityIds>? EntityId, PublicId<AuditEntryIds>? Before, int? Take);

/// <summary>The rules of <see cref="AuditTrailListQuery"/>.</summary>
public sealed class AuditTrailListQueryValidator : IValidator<AuditTrailListQuery>
{
    /// <inheritdoc />
    public ValidationErrors Validate(AuditTrailListQuery input)
    {
        ArgumentNullException.ThrowIfNull(input);
        var errors = new ValidationErrors();
        if (input.EntityType is { } entityType)
        {
            errors.RequireText(FieldNames.Of(nameof(input.EntityType)), entityType, AuditPaging.EntityTypeMaxLength);
        }
        else if (input.EntityId is { HasValue: true })
        {
            errors.Add(FieldNames.Of(nameof(input.EntityType)), ValidationMessages.Required);
        }

        if (input.Take is { } take && (take < 1 || take > AuditPaging.MaxTake))
        {
            errors.Add(FieldNames.Of(nameof(input.Take)), ValidationMessages.InvalidFormat);
        }

        return errors;
    }
}

/// <summary>One audited change as the viewer shows it: every internal id is a PublicId, every value already masked.</summary>
/// <param name="Id">The entry (also the cursor of the next page).</param>
/// <param name="EntityType">The audited entity type name.</param>
/// <param name="EntityId">The audited row, or null when its key is not a single integer (a composite key).</param>
/// <param name="Action">What happened (an int on the wire).</param>
/// <param name="Actor">The acting subject id, or <c>SYSTEM</c>.</param>
/// <param name="At">When the change was saved (UTC).</param>
/// <param name="Changes">The recorded values.</param>
public sealed record AuditEntryView(
    PublicId<AuditEntryIds> Id,
    string EntityType,
    PublicId<AuditedEntityIds>? EntityId,
    AuditAction Action,
    string Actor,
    DateTimeOffset At,
    IReadOnlyList<AuditChangeView> Changes);

/// <summary>One recorded value; an identifier value is the token of a <c>PublicId&lt;AuditedEntityIds&gt;</c>, never a number.</summary>
/// <param name="Property">The property name.</param>
/// <param name="OldValue">The value before, or null.</param>
/// <param name="NewValue">The value after, or null.</param>
public sealed record AuditChangeView(string Property, string? OldValue, string? NewValue);

/// <summary>One page of the viewer, newest first.</summary>
/// <param name="Items">The entries.</param>
/// <param name="HasMore">True when older entries exist; ask again with <c>before</c> = the last item's id.</param>
public sealed record AuditEntryViewPage(IReadOnlyList<AuditEntryView> Items, bool HasMore);

/// <summary>
/// <c>GET /api/admin/audit</c>: one page of the audit trail, newest first, optionally one entity type or one row of it.
/// </summary>
public sealed class ListAuditTrail(IValidator<AuditTrailListQuery> validator, IAuditTrailQueries trail)
{
    /// <summary>The page, or <see cref="AuditErrorCodes.InvalidFilter"/> with the field errors (400).</summary>
    public async Task<Outcome<AuditEntryViewPage>> ExecuteAsync(AuditTrailListQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        var errors = validator.Validate(query);
        if (!errors.IsValid)
        {
            return Refusal.Invalid(errors.ToDictionary(), AuditErrorCodes.InvalidFilter);
        }

        var page = await trail.FindAsync(ToPortQuery(query), cancellationToken);
        return new AuditEntryViewPage([.. page.Items.Select(AuditViews.Of)], page.HasMore);
    }

    /// <summary>The port's query of a valid <paramref name="query"/>.</summary>
    public static AuditTrailQuery ToPortQuery(AuditTrailListQuery query)
    {
        ArgumentNullException.ThrowIfNull(query);
        var entityType = string.IsNullOrWhiteSpace(query.EntityType) ? null : query.EntityType.Trim();
        var entityKey = entityType is not null && query.EntityId is { HasValue: true } entityId
            ? entityId.Value.ToString(CultureInfo.InvariantCulture)
            : null;
        var before = query.Before is { HasValue: true } cursor ? cursor.Value : (long?)null;
        return new AuditTrailQuery(entityType, entityKey, before, query.Take ?? AuditPaging.DefaultTake);
    }
}

/// <summary>The viewer's form of a trail entry: internal ids become PublicIds (the codec of the host).</summary>
public static class AuditViews
{
    /// <summary>The view of <paramref name="entry"/>.</summary>
    public static AuditEntryView Of(AuditTrailEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);
        return new AuditEntryView(
            Id: PublicId<AuditEntryIds>.From(entry.Id),
            EntityType: entry.EntityType,
            EntityId: IdentifierOf(entry.EntityKey) is { } key ? PublicId<AuditedEntityIds>.From(key) : null,
            Action: entry.Action,
            Actor: entry.Actor,
            At: entry.At,
            Changes: [.. entry.Changes.Select(Of)]);
    }

    /// <summary>The view of <paramref name="change"/>: an identifier value becomes a token.</summary>
    public static AuditChangeView Of(AuditPropertyChange change)
    {
        ArgumentNullException.ThrowIfNull(change);
        return change.IsIdentifier
            ? new AuditChangeView(change.Property, TokenOf(change.OldValue), TokenOf(change.NewValue))
            : new AuditChangeView(change.Property, change.OldValue, change.NewValue);
    }

    /// <summary>The internal id in <paramref name="value"/> (a positive integer), or null.</summary>
    public static long? IdentifierOf(string? value) =>
        long.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var id) && id >= 1 ? id : null;

    private static string? TokenOf(string? value) =>
        IdentifierOf(value) is { } id ? PublicId<AuditedEntityIds>.From(id).ToToken() : null;
}
