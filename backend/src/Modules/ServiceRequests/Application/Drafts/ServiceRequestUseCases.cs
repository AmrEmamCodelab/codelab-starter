using Starter.Application.Records;
using Starter.Domain.Records;
using Starter.Application.Results;
using Starter.Modules.ServiceRequests.Application.Access;
using Starter.Modules.ServiceRequests.Application.Validation;
using Starter.Modules.ServiceRequests.Domain;

namespace Starter.Modules.ServiceRequests.Application.Drafts;

/// <summary>A draft as the wizard resumes it: the stored fields of each saved step (null until saved).</summary>
/// <param name="RecordId">The record id.</param>
/// <param name="CategoryToken">The category's PublicId token, or null.</param>
/// <param name="Title">The title, or null.</param>
/// <param name="Description">The description, or null.</param>
/// <param name="ContactPhone">The contact phone, or null.</param>
/// <param name="PreferredDate">The preferred date (<c>YYYY-MM-DD</c>), or null.</param>
public sealed record ServiceRequestDraftView(
    long RecordId,
    string? CategoryToken,
    string? Title,
    string? Description,
    string? ContactPhone,
    string? PreferredDate);

/// <summary>A service request's own fields for the detail cards (requester and staff).</summary>
/// <param name="Draft">The fields (null ones only while a draft).</param>
/// <param name="Number">The reference number, or null before the submit.</param>
/// <param name="CategoryNameAr">The category's Arabic name, or null when no category is set or the value was retired.</param>
/// <param name="CategoryNameEn">The category's English name, or null likewise.</param>
public sealed record ServiceRequestDetailsView(
    ServiceRequestDraftView Draft,
    string? Number,
    string? CategoryNameAr,
    string? CategoryNameEn);

/// <summary>Maps a stored row to its wire view (one owner of the token and date formats).</summary>
public sealed class ServiceRequestViews(ServiceRequestCategories categories)
{
    /// <summary>The view of record <paramref name="recordId"/>'s row; every field null when no step was saved yet.</summary>
    public ServiceRequestDraftView DraftOf(long recordId, ServiceRequest? row) => new(
        RecordId: recordId,
        CategoryToken: row?.CategoryId is { } category ? categories.TokenOf(category) : null,
        Title: row?.Title,
        Description: row?.Description,
        ContactPhone: row?.ContactPhone,
        PreferredDate: row?.PreferredDate is { } date ? ServiceRequestRules.FormatDate(date) : null);
}

/// <summary>
/// Creates a draft service request for the caller: the Records kernel creates the record (status Draft, owner the
/// caller, no number yet) and this module adds its empty row. Returns the record id.
/// </summary>
public sealed class CreateServiceRequestDraft(IRecordLifecycle lifecycle, IServiceRequestStore store)
{
    /// <summary>Runs the use case.</summary>
    public async Task<Outcome<long>> ExecuteAsync(CancellationToken cancellationToken)
    {
        var record = await lifecycle.CreateDraftAsync(ServiceRequestRecord.RecordType, cancellationToken);
        store.Add(ServiceRequest.StartFor(record.Id));
        await store.SaveChangesAsync(cancellationToken);
        return record.Id;
    }
}

/// <summary>The caller's own draft for the wizard to resume; 404 <see cref="RecordsErrorCodes.NotFound"/> when missing or not the caller's.</summary>
public sealed class GetServiceRequestDraft(ServiceRequestAccess access, IServiceRequestStore store, ServiceRequestViews views)
{
    /// <summary>Runs the use case.</summary>
    public async Task<Outcome<ServiceRequestDraftView>> ExecuteAsync(long recordId, CancellationToken cancellationToken)
    {
        if (await access.FindOwnedAsync(recordId, cancellationToken) is null)
        {
            return Refusal.NotFound(RecordsErrorCodes.NotFound);
        }

        return views.DraftOf(recordId, await store.FindAsync(recordId, cancellationToken));
    }
}

/// <summary>
/// Submits the caller's draft through the Records kernel, which checks the owner and the status, runs
/// <see cref="ServiceRequestSubmitValidator"/> over the whole request (a crafted submit that skipped a step is 400
/// <c>VALIDATION_FAILED</c> with that step's field errors), applies the required-attachments interlock (409
/// <c>ATTACHMENTS_REQUIRED_MISSING</c>) and allocates the number (<c>SR-2026-001</c>). Returns the number.
/// </summary>
public sealed class SubmitServiceRequest(IRecordLifecycle lifecycle)
{
    /// <summary>Runs the use case.</summary>
    public async Task<Outcome<string>> ExecuteAsync(long recordId, CancellationToken cancellationToken)
    {
        if (recordId < 1)
        {
            return Refusal.NotFound(RecordsErrorCodes.NotFound);
        }

        var result = await lifecycle.SubmitAsync(new RecordRef(recordId, ServiceRequestRecord.RecordType), cancellationToken);
        return result.ToOutcome();
    }
}

/// <summary>
/// The fields of a service request for its detail cards, for its owner (with <c>service-requests.view-own</c>) or a
/// queue holder whose scope reaches it; 404 <see cref="RecordsErrorCodes.NotFound"/> otherwise.
/// </summary>
public sealed class GetServiceRequestDetails(
    ServiceRequestAccess access,
    IServiceRequestStore store,
    ServiceRequestCategories categories,
    ServiceRequestViews views)
{
    /// <summary>Runs the use case.</summary>
    public async Task<Outcome<ServiceRequestDetailsView>> ExecuteAsync(long recordId, CancellationToken cancellationToken)
    {
        var record = await access.FindViewableAsync(recordId, cancellationToken);
        if (record is null)
        {
            return Refusal.NotFound(RecordsErrorCodes.NotFound);
        }

        var row = await store.FindAsync(recordId, cancellationToken);
        var category = row?.CategoryId is { } categoryId ? await categories.FindActiveAsync(categoryId, cancellationToken) : null;
        return new ServiceRequestDetailsView(
            Draft: views.DraftOf(recordId, row),
            Number: record.Number,
            CategoryNameAr: category?.NameAr,
            CategoryNameEn: category?.NameEn);
    }
}
