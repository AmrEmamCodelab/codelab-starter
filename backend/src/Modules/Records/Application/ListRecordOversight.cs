using Starter.Application.Records;
using Starter.Application.Results;
using Starter.Application.Validation;
using Starter.Modules.Records.Domain;

namespace Starter.Modules.Records.Application;

/// <summary>The query of <c>GET api/records/{recordType}/oversight</c>.</summary>
/// <param name="RecordType">The record type from the route.</param>
/// <param name="Status">Only this status, or null.</param>
/// <param name="Number">Only numbers containing this text, or null.</param>
/// <param name="Page">The 1-based page, or null for the first.</param>
/// <param name="PageSize">The page size, or null for the default.</param>
public sealed record OversightQuery(string? RecordType, int? Status, string? Number, int? Page, int? PageSize);

/// <summary>The rules of an oversight query (pure; the use case calls it first).</summary>
public sealed class OversightQueryValidator : IValidator<OversightQuery>
{
    /// <inheritdoc />
    public ValidationErrors Validate(OversightQuery input)
    {
        ArgumentNullException.ThrowIfNull(input);
        var errors = new ValidationErrors();
        return input.Number?.Length > RecordColumnLimits.NumberMaxLength
            ? errors.Add(FieldNames.Of(nameof(OversightQuery.Number)), ValidationMessages.TooLong)
            : errors;
    }
}

/// <summary>
/// <c>GET api/records/{recordType}/oversight</c> (<see cref="RecordsPermissionKeys.OversightView"/>): every submitted
/// record of one type with its owner, newest first, narrowed to the caller's organization when the permission is held at
/// that scope. Read-only: no allowed actions are computed.
/// </summary>
public sealed class ListRecordOversight(
    RecordTypes types, RecordStandings standings, IRecordStore store, RecordUserNames names, IValidator<OversightQuery> validator)
{
    /// <summary>The oversight page.</summary>
    public async Task<Outcome<RecordListPage>> ExecuteAsync(OversightQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        var errors = validator.Validate(query);
        if (!errors.IsValid)
        {
            return Refusal.Invalid(errors);
        }

        var type = types.Find(query.RecordType);
        if (type is null)
        {
            return Refusal.NotFound(RecordsErrorCodes.NotFound);
        }

        var paging = RecordPageRequest.From(query.Page, query.PageSize);
        var scope = (await standings.ScopeForAsync(RecordsPermissionKeys.OversightView, cancellationToken)).Narrow(requestedOrganizationId: null);
        if (scope.MatchesNothing)
        {
            return new RecordListPage([], paging.Page, paging.PageSize, Total: 0);
        }

        var filter = new RecordQuery(
            type.RecordType,
            ListMyRecords.StatusFilter(query.Status),
            NumberContains: string.IsNullOrWhiteSpace(query.Number) ? null : query.Number.Trim(),
            OrganizationId: scope.OrganizationId,
            SubmittedFrom: null,
            SubmittedTo: null,
            Ids: null,
            Page: paging.Page,
            PageSize: paging.PageSize);
        var page = await store.PageAsync(new RecordPageQuery(filter, OwnerUserId: null, SubmittedOnly: true), cancellationToken);
        var owners = await names.ForAsync(page.Items.Select(record => record.OwnerUserId), cancellationToken);
        return RecordLists.PageOf(type, page, owners);
    }
}
